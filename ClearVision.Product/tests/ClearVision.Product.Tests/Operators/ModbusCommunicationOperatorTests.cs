using System.Net;
using System.Net.Sockets;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NModbus;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
[Collection("PLC Operator Integration")]
public class ModbusCommunicationOperatorTests
{
    private readonly ModbusCommunicationOperator _operator;

    public ModbusCommunicationOperatorTests()
    {
        _operator = CreateSut();
    }

    [Fact]
    public void OperatorType_ShouldBeModbusCommunication()
    {
        _operator.OperatorType.Should().Be(OperatorType.ModbusCommunication);
    }

    [Fact]
    public void ValidateParameters_WithAuthoritativeProfile_ShouldBeValid()
    {
        var op = CreateOperator();
        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithRawPort_ShouldReturnInvalid()
    {
        var op = CreateOperator();
        op.AddParameter(new(Guid.NewGuid(), "Port", "Port", "", "int", 70000, 0, 65535, true));
        var result = _operator.ValidateParameters(op);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().StartWith("PLC_RAW_TARGET_FORBIDDEN:");
    }

    [Fact]
    public void ValidateParameters_WithRawSlaveId_ShouldReturnInvalid()
    {
        var op = CreateOperator();
        op.AddParameter(new(Guid.NewGuid(), "SlaveId", "SlaveId", "", "int", 256, 0, 255, true));
        var result = _operator.ValidateParameters(op);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().StartWith("PLC_RAW_TARGET_FORBIDDEN:");
    }

    [Fact]
    public void ValidateParameters_WithTcpGatewayUnitId255_ShouldBeValid()
    {
        var sut = CreateSut(unitId: 255);
        var op = CreateOperator();

        var result = sut.ValidateParameters(op);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithLegacyRawProtocol_ShouldReturnFailure()
    {
        var op = CreateOperator();
        op.AddParameter(TestHelpers.CreateParameter("Protocol", "RTU", "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("PLC_RAW_TARGET_FORBIDDEN:");
    }

    [Fact]
    public async Task ExecuteAsync_WithServerProfile_ShouldDispatchOnlyResolvedEndpoint()
    {
        ModbusCommunicationOperator.ModbusDispatchRequest? captured = null;
        var sut = CreateSut(
            host: "192.0.2.45",
            port: 1502,
            unitId: 17,
            dispatchOverride: (request, _) =>
            {
                captured = request;
                return Task.FromResult(("42", true));
            });
        var op = CreateOperator(registerAddress: 10);

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        captured.Should().NotBeNull();
        captured!.Host.Should().Be("192.0.2.45");
        captured.Port.Should().Be(1502);
        captured.UnitId.Should().Be(17);
        captured.RegisterAddress.Should().Be(10);
        captured.FunctionCode.Should().Be("ReadHolding");
    }

    [Fact]
    public async Task ExecuteAsync_WithRawEndpoint_ShouldRejectBeforeDispatchHandler()
    {
        var dispatchCalls = 0;
        var sut = CreateSut(dispatchOverride: (_, _) =>
        {
            dispatchCalls++;
            return Task.FromResult(("unexpected", true));
        });
        var op = CreateOperator();
        op.AddParameter(TestHelpers.CreateParameter("IpAddress", "203.0.113.77", "string"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("PLC_RAW_TARGET_FORBIDDEN:");
        dispatchCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithFailedTcpConnect_ShouldNotRetainPoolsOrKeyLocks()
    {
        await ModbusCommunicationOperator.ResetConnectionPoolPolicyForTestingAsync();
        var port = GetUnusedLoopbackPort();
        var sut = CreateSut(port: port);
        var op = CreateOperator();
        op.AddParameter(TestHelpers.CreateParameter("TimeoutMs", 100, "int"));

        try
        {
            var result = await sut.ExecuteAsync(op, new Dictionary<string, object>());

            result.IsSuccess.Should().BeFalse();
            var snapshot = ModbusCommunicationOperator.GetConnectionPoolSnapshot();
            snapshot.PooledCount.Should().Be(0);
            snapshot.RetiringCount.Should().Be(0);
            snapshot.PendingConnectionCount.Should().Be(0);
            snapshot.ActiveLeaseCount.Should().Be(0);
            snapshot.ConnectionKeyLockCount.Should().Be(0);
            snapshot.OperationLockCount.Should().Be(0);
        }
        finally
        {
            await ModbusCommunicationOperator.ResetConnectionPoolPolicyForTestingAsync();
        }
    }

    [Fact]
    public async Task ConnectionPool_ClearDuringActiveLease_ShouldRetireThenDisposeExactlyOnce()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero));
        await ModbusCommunicationOperator.ConfigureConnectionPoolForTestingAsync(
            capacity: 2,
            maxIdleConnectionAge: TimeSpan.FromMinutes(5),
            clock);
        var resource = new FakeModbusConnectionResource();
        var leaseAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = HoldLeaseUntilReleasedAsync(
            "modbus:active-clear",
            resource,
            leaseAcquired,
            releaseOperation.Task);

        try
        {
            await leaseAcquired.Task.WaitAsync(TimeSpan.FromSeconds(5));

            ModbusCommunicationOperator.ClearConnectionPool();

            var retiring = ModbusCommunicationOperator.GetConnectionPoolSnapshot();
            retiring.PooledCount.Should().Be(0);
            retiring.RetiringCount.Should().Be(1);
            retiring.ActiveLeaseCount.Should().Be(1);
            retiring.ClearRetirementCount.Should().Be(1);
            resource.DisposeCount.Should().Be(0);

            releaseOperation.TrySetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));

            resource.DisposeCount.Should().Be(1);
            ModbusCommunicationOperator.ClearConnectionPool();
            resource.DisposeCount.Should().Be(1);
            var completed = ModbusCommunicationOperator.GetConnectionPoolSnapshot();
            completed.PooledCount.Should().Be(0);
            completed.RetiringCount.Should().Be(0);
            completed.ActiveLeaseCount.Should().Be(0);
            completed.DisposedConnectionCount.Should().Be(1);
            completed.ConnectionKeyLockCount.Should().Be(0);
        }
        finally
        {
            releaseOperation.TrySetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            await ModbusCommunicationOperator.ResetConnectionPoolPolicyForTestingAsync();
        }
    }

    [Fact]
    public async Task ConnectionPool_CapacityAndIdleMaintenance_ShouldEvictLruAndRecoverCounts()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 31, 1, 0, 0, TimeSpan.Zero));
        await ModbusCommunicationOperator.ConfigureConnectionPoolForTestingAsync(
            capacity: 2,
            maxIdleConnectionAge: TimeSpan.FromMinutes(5),
            clock);
        var first = new FakeModbusConnectionResource();
        var second = new FakeModbusConnectionResource();
        var third = new FakeModbusConnectionResource();

        try
        {
            await AcquireAndReleaseAsync("modbus:lru-first", first);
            clock.Advance(TimeSpan.FromMinutes(1));
            await AcquireAndReleaseAsync("modbus:lru-second", second);
            clock.Advance(TimeSpan.FromMinutes(1));

            await using (var thirdLease = await ModbusCommunicationOperator.AcquireConnectionLeaseForTestingAsync(
                             "modbus:lru-third",
                             _ => Task.FromResult<IModbusConnectionResource>(third)))
            {
                first.DisposeCount.Should().Be(1);
                second.DisposeCount.Should().Be(0);
                third.DisposeCount.Should().Be(0);
                var capacity = ModbusCommunicationOperator.GetConnectionPoolSnapshot();
                capacity.PooledCount.Should().Be(2);
                capacity.CapacityEvictionCount.Should().Be(1);
                capacity.ActiveLeaseCount.Should().Be(1);
            }

            clock.Advance(TimeSpan.FromMinutes(10));
            await ModbusCommunicationOperator.RunConnectionPoolMaintenanceForTestingAsync();

            second.DisposeCount.Should().Be(1);
            third.DisposeCount.Should().Be(1);
            var completed = ModbusCommunicationOperator.GetConnectionPoolSnapshot();
            completed.PooledCount.Should().Be(0);
            completed.RetiringCount.Should().Be(0);
            completed.ActiveLeaseCount.Should().Be(0);
            completed.IdleEvictionCount.Should().Be(2);
            completed.CapacityEvictionCount.Should().Be(1);
            completed.DisposedConnectionCount.Should().Be(3);
        }
        finally
        {
            await ModbusCommunicationOperator.ResetConnectionPoolPolicyForTestingAsync();
        }
    }

    [Fact]
    public async Task ConnectionPool_DisconnectDuringLease_ShouldRetireUntilFinalRelease()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 31, 2, 0, 0, TimeSpan.Zero));
        await ModbusCommunicationOperator.ConfigureConnectionPoolForTestingAsync(
            capacity: 2,
            maxIdleConnectionAge: TimeSpan.FromMinutes(5),
            clock);
        var resource = new FakeModbusConnectionResource();
        var lease = await ModbusCommunicationOperator.AcquireConnectionLeaseForTestingAsync(
            "modbus:disconnect",
            _ => Task.FromResult<IModbusConnectionResource>(resource));

        try
        {
            resource.MarkDisconnected();
            await ModbusCommunicationOperator.RunConnectionPoolMaintenanceForTestingAsync();

            var retiring = ModbusCommunicationOperator.GetConnectionPoolSnapshot();
            retiring.PooledCount.Should().Be(0);
            retiring.RetiringCount.Should().Be(1);
            retiring.ActiveLeaseCount.Should().Be(1);
            retiring.DisconnectedRemovalCount.Should().Be(1);
            resource.DisposeCount.Should().Be(0);

            await lease.DisposeAsync();
            await lease.DisposeAsync();

            resource.DisposeCount.Should().Be(1);
            var completed = ModbusCommunicationOperator.GetConnectionPoolSnapshot();
            completed.RetiringCount.Should().Be(0);
            completed.ActiveLeaseCount.Should().Be(0);
            completed.DisposedConnectionCount.Should().Be(1);
        }
        finally
        {
            await lease.DisposeAsync();
            await ModbusCommunicationOperator.ResetConnectionPoolPolicyForTestingAsync();
        }
    }

    [Fact]
    public async Task ConnectionPool_WhenAllCapacityIsLeased_ShouldRejectBeforeFactory()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 31, 3, 0, 0, TimeSpan.Zero));
        await ModbusCommunicationOperator.ConfigureConnectionPoolForTestingAsync(
            capacity: 1,
            maxIdleConnectionAge: TimeSpan.FromMinutes(5),
            clock);
        var active = new FakeModbusConnectionResource();
        var secondFactoryCalls = 0;
        var activeLease = await ModbusCommunicationOperator.AcquireConnectionLeaseForTestingAsync(
            "modbus:capacity-active",
            _ => Task.FromResult<IModbusConnectionResource>(active));

        try
        {
            var act = async () => await ModbusCommunicationOperator.AcquireConnectionLeaseForTestingAsync(
                "modbus:capacity-rejected",
                _ =>
                {
                    Interlocked.Increment(ref secondFactoryCalls);
                    return Task.FromResult<IModbusConnectionResource>(new FakeModbusConnectionResource());
                });

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("MODBUS_CONNECTION_POOL_CAPACITY_REACHED:*");
            secondFactoryCalls.Should().Be(0);
            var snapshot = ModbusCommunicationOperator.GetConnectionPoolSnapshot();
            snapshot.PooledCount.Should().Be(1);
            snapshot.ActiveLeaseCount.Should().Be(1);
            snapshot.PendingConnectionCount.Should().Be(0);
        }
        finally
        {
            await activeLease.DisposeAsync();
            await ModbusCommunicationOperator.ResetConnectionPoolPolicyForTestingAsync();
        }

        active.DisposeCount.Should().Be(1);
    }

    private static int GetUnusedLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static Operator CreateOperator(int registerAddress = 0)
    {
        var op = new Operator("test", OperatorType.ModbusCommunication, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ProfileId", "modbus-main", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RegisterAddress", registerAddress, "int"));
        op.AddParameter(TestHelpers.CreateParameter("RegisterCount", 1, "int"));
        op.AddParameter(TestHelpers.CreateParameter("FunctionCode", "ReadHolding", "string"));
        return op;
    }

    private static ModbusCommunicationOperator CreateSut(
        string host = "127.0.0.1",
        int port = 502,
        int unitId = 1,
        Func<ModbusCommunicationOperator.ModbusDispatchRequest, CancellationToken, Task<(string response, bool status)>>? dispatchOverride = null)
    {
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.GetCurrent().Returns(new AppConfig
        {
            ExecutionResources = new ExecutionResourceProfilesConfig
            {
                PlcProfiles =
                [
                    new PlcExecutionResourceProfile
                    {
                        Id = "modbus-main",
                        Enabled = true,
                        Protocol = ExecutionPlcProtocols.ModbusTcp,
                        Host = host,
                        Port = port,
                        UnitId = unitId,
                        Bindings =
                        [
                            new PlcExecutionResourceBinding
                            {
                                Address = "0",
                                DataType = "Word",
                                CanRead = true,
                                CanWrite = true,
                                MaxElementCount = 125,
                                AllowedFunctionCodes =
                                [
                                    "ReadCoils",
                                    "ReadHolding",
                                    "WriteSingle",
                                    "WriteMultiple"
                                ]
                            },
                            new PlcExecutionResourceBinding
                            {
                                Address = "10",
                                DataType = "Word",
                                CanRead = true,
                                CanWrite = true,
                                MaxElementCount = 125,
                                AllowedFunctionCodes =
                                [
                                    "ReadCoils",
                                    "ReadHolding",
                                    "WriteSingle",
                                    "WriteMultiple"
                                ]
                            }
                        ]
                    }
                ]
            }
        });
        var resolver = new ServerExecutionResourceProfileResolver(configurationService);
        return new ModbusCommunicationOperator(
            Substitute.For<ILogger<ModbusCommunicationOperator>>(),
            resolver,
            dispatchOverride);
    }

    private static async Task AcquireAndReleaseAsync(
        string connectionKey,
        IModbusConnectionResource resource)
    {
        await using var lease = await ModbusCommunicationOperator.AcquireConnectionLeaseForTestingAsync(
            connectionKey,
            _ => Task.FromResult(resource));
    }

    private static async Task HoldLeaseUntilReleasedAsync(
        string connectionKey,
        IModbusConnectionResource resource,
        TaskCompletionSource leaseAcquired,
        Task release)
    {
        await using var lease = await ModbusCommunicationOperator.AcquireConnectionLeaseForTestingAsync(
            connectionKey,
            _ => Task.FromResult(resource));
        leaseAcquired.TrySetResult();
        await release;
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

    private sealed class FakeModbusConnectionResource : IModbusConnectionResource
    {
        private int _connected = 1;
        private int _disposeCount;

        public FakeModbusConnectionResource()
        {
            Master = Substitute.For<IModbusMaster>();
        }

        public IModbusMaster Master { get; }

        public bool IsConnected => Volatile.Read(ref _connected) != 0;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void ApplyTimeouts(int timeoutMs)
        {
        }

        public void MarkDisconnected() => Volatile.Write(ref _connected, 0);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            Volatile.Write(ref _connected, 0);
        }
    }
}
