using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Logging;
using NModbus;

namespace ClearVision.Product.Infrastructure.Operators;

public sealed record ModbusConnectionPoolSnapshot(
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
    long CreatedConnectionCount,
    long DisposedConnectionCount,
    long ClearRetirementCount,
    long IdleEvictionCount,
    long CapacityEvictionCount,
    long DisconnectedRemovalCount);

internal interface IModbusConnectionResource : IDisposable
{
    IModbusMaster Master { get; }

    bool IsConnected { get; }

    void ApplyTimeouts(int timeoutMs);
}

[OperatorMeta(
    DisplayName = "Modbus TCP通信",
    Description = "通过 Modbus TCP 读写线圈和保持寄存器；当前算子不执行 Modbus RTU 通信。",
    CategoryId = OperatorCategoryId.Communication,
    IconName = "modbus",
    Keywords = new[] { "Modbus", "PLC", "Communication", "Register", "RTU", "TCP", "Industrial", "Modbus通信", "Modbus Communication" },
    Version = "1.1.0"
)]
[OperatorParameterRule("ProfileId", RequiredPolicy = OperatorParameterRequiredPolicy.Required, ResourceKind = OperatorResourceKind.PlcProfile, ReasonCode = "MODBUS_PLC_PROFILE_REQUIRED")]
[OperatorParameterRule("RegisterAddress", RequiredPolicy = OperatorParameterRequiredPolicy.Required, ResourceKind = OperatorResourceKind.PlcAddress, ReasonCode = "MODBUS_REGISTER_ADDRESS_REQUIRED")]
[OperatorParameterRule("FunctionCode", RequiredPolicy = OperatorParameterRequiredPolicy.Required, ReasonCode = "MODBUS_FUNCTION_CODE_REQUIRED")]
[OperatorParameterRule("RegisterCount", EnabledWhenAny = new[] { "FunctionCode==ReadCoils", "FunctionCode==ReadHolding" }, HiddenWhenAny = new[] { "FunctionCode==WriteSingle", "FunctionCode==WriteMultiple" }, IgnoredWhenAny = new[] { "FunctionCode==WriteSingle", "FunctionCode==WriteMultiple" }, ReasonCode = "MODBUS_REGISTER_COUNT_ONLY_FOR_READ")]
[OperatorParameterRule("WriteValue", RequiredWhenAny = new[] { "FunctionCode==WriteSingle", "FunctionCode==WriteMultiple" }, EnabledWhenAny = new[] { "FunctionCode==WriteSingle", "FunctionCode==WriteMultiple" }, HiddenWhenAny = new[] { "FunctionCode==ReadCoils", "FunctionCode==ReadHolding" }, IgnoredWhenAny = new[] { "FunctionCode==ReadCoils", "FunctionCode==ReadHolding" }, ReasonCode = "MODBUS_WRITE_VALUE_ONLY_FOR_WRITE")]
[InputPort("Data", "Data", PortDataType.Any, IsRequired = false)]
[OutputPort("Response", "Response", PortDataType.String)]
[OutputPort("Status", "Status", PortDataType.Boolean)]
[OperatorParam("ProfileId", "PLC Profile", "string", DefaultValue = "")]
[OperatorParam("RegisterAddress", "Register Address", "int", DefaultValue = 0)]
[OperatorParam("RegisterCount", "Register Count", "int", DefaultValue = 1, Min = 1, Max = 125)]
[OperatorParam("FunctionCode", "Function Code", "enum", DefaultValue = "ReadHolding", Options = new[] { "ReadCoils|Read Coils", "ReadHolding|Read Holding Registers", "WriteSingle|Write Single Register", "WriteMultiple|Write Multiple Registers" })]
[OperatorParam("WriteValue", "Write Value", "string", DefaultValue = "")]
[OperatorParam("TimeoutMs", "Timeout (ms)", "int", DefaultValue = 5000, Min = 100, Max = 60000)]
public class ModbusCommunicationOperator : OperatorBase
{
    private const int DefaultOperationTimeoutMs = 5000;
    private const int DefaultMaxPooledConnections = 32;
    private static readonly TimeSpan DefaultMaxIdleConnectionAge = TimeSpan.FromMinutes(10);

    private static readonly object PoolSync = new();
    private static readonly Dictionary<string, PooledModbusConnectionEntry> ConnectionPool =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<PooledModbusConnectionEntry, byte> RetiringConnections = new();
    private static readonly ConcurrentDictionary<string, RefCountedSemaphore> ConnectionLocks = new();
    private static readonly ConcurrentDictionary<string, RefCountedSemaphore> OperationLocks = new();
    private static readonly IModbusFactory ModbusFactory = new ModbusFactory();
    private static TimeProvider PoolTimeProvider = TimeProvider.System;
    private static TimeSpan MaxIdleConnectionAge = DefaultMaxIdleConnectionAge;
    private static int MaxPooledConnections = DefaultMaxPooledConnections;
    private static int PendingConnectionCount;
    private static long PoolGeneration;
    private static long CreatedConnectionCount;
    private static long DisposedConnectionCount;
    private static long ClearRetirementCount;
    private static long IdleEvictionCount;
    private static long CapacityEvictionCount;
    private static long DisconnectedRemovalCount;

    private readonly IExecutionResourceProfileResolver _executionResourceProfileResolver;
    private readonly Func<ModbusDispatchRequest, CancellationToken, Task<(string response, bool status)>>? _dispatchOverride;

    public ModbusCommunicationOperator(ILogger<ModbusCommunicationOperator> logger)
        : this(logger, DenyAllExecutionResourceProfileResolver.Instance, dispatchOverride: null)
    {
    }

    public ModbusCommunicationOperator(
        ILogger<ModbusCommunicationOperator> logger,
        IExecutionResourceProfileResolver executionResourceProfileResolver)
        : this(logger, executionResourceProfileResolver, dispatchOverride: null)
    {
    }

    internal ModbusCommunicationOperator(
        ILogger<ModbusCommunicationOperator> logger,
        IExecutionResourceProfileResolver executionResourceProfileResolver,
        Func<ModbusDispatchRequest, CancellationToken, Task<(string response, bool status)>>? dispatchOverride)
        : base(logger)
    {
        _executionResourceProfileResolver = executionResourceProfileResolver ??
            throw new ArgumentNullException(nameof(executionResourceProfileResolver));
        _dispatchOverride = dispatchOverride;
    }

    public override OperatorType OperatorType => OperatorType.ModbusCommunication;

    protected override async Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var forbiddenRawTarget = FindForbiddenRawTargetParameter(@operator);
        if (forbiddenRawTarget != null)
        {
            return OperatorExecutionOutput.Failure(
                $"PLC_RAW_TARGET_FORBIDDEN: {forbiddenRawTarget} cannot grant execution authority; use ProfileId and an allow-listed RegisterAddress/FunctionCode binding.");
        }

        var profileId = GetStringParam(@operator, "ProfileId", string.Empty);
        var protocol = GetStringParam(@operator, "Protocol", "TCP");
        var registerAddress = GetIntParam(@operator, "RegisterAddress", 0);
        var registerCount = GetIntParam(@operator, "RegisterCount", 1, 1, 125);
        var functionCode = GetStringParam(@operator, "FunctionCode", "ReadHolding");
        // 写操作优先使用上游连线到 "Data" 端口的动态值，否则回退参数面板中的静态 WriteValue。
        var writeValue = ResolveWriteValue(@operator, inputs);
        var timeoutMs = GetIntParam(@operator, "TimeoutMs", DefaultOperationTimeoutMs, 100, 60000);

        if (!protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase))
        {
            return OperatorExecutionOutput.Failure("Modbus RTU requires serial-port lifecycle configuration and is not supported by this package operator.");
        }

        var effectiveElementCount = functionCode switch
        {
            "WriteSingle" => 1,
            "WriteMultiple" when TryParseRegisterValues(writeValue, out var values) => values.Length,
            "WriteMultiple" => 0,
            _ => registerCount
        };
        if (effectiveElementCount == 0)
        {
            return OperatorExecutionOutput.Failure("WriteValue must be a comma-separated list of unsigned 16-bit integers.");
        }

        var resolution = _executionResourceProfileResolver.ResolvePlc(
            profileId,
            new PlcExecutionResourceRequest(
                ExecutionPlcProtocols.ModbusTcp,
                registerAddress.ToString(System.Globalization.CultureInfo.InvariantCulture),
                functionCode,
                effectiveElementCount));
        if (!resolution.Resolved || resolution.Resource == null)
        {
            return OperatorExecutionOutput.Failure($"{resolution.Code}: {resolution.Message}");
        }

        var resource = resolution.Resource;
        var ipAddress = resource.Host;
        var port = resource.Port;
        var slaveId = resource.UnitId;
        registerAddress = int.Parse(resource.Address, System.Globalization.CultureInfo.InvariantCulture);
        functionCode = resource.Operation;

        var dispatchRequest = new ModbusDispatchRequest(
            ipAddress,
            port,
            slaveId,
            functionCode,
            registerAddress,
            registerCount,
            writeValue,
            timeoutMs);
        var (response, status) = _dispatchOverride != null
            ? await _dispatchOverride(dispatchRequest, cancellationToken)
            : await ExecuteTcpModbusAsync(
                dispatchRequest.Host,
                dispatchRequest.Port,
                dispatchRequest.UnitId,
                dispatchRequest.FunctionCode,
                dispatchRequest.RegisterAddress,
                dispatchRequest.RegisterCount,
                dispatchRequest.WriteValue,
                dispatchRequest.TimeoutMs,
                cancellationToken);

        if (!status)
        {
            return OperatorExecutionOutput.Failure(response);
        }

        return OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["Response"] = response,
            ["Status"] = status,
            ["Protocol"] = protocol,
            ["FunctionCode"] = functionCode,
            ["SlaveId"] = slaveId
        });
    }

    /// <summary>
    /// 解析写入寄存器的值：优先读取上游连线（"Data" 端口，以及展平合并进 inputs 的常见结果键），
    /// 无连线时回退到参数面板中的静态 <c>WriteValue</c>。仅影响写功能码，读功能码不使用该值。
    /// </summary>
    private string ResolveWriteValue(Operator @operator, Dictionary<string, object>? inputs)
    {
        var staticValue = GetStringParam(@operator, "WriteValue", string.Empty);

        if (inputs == null || inputs.Count == 0)
        {
            return staticValue;
        }

        foreach (var key in new[] { "Data", "JudgmentValue", "Value" })
        {
            if (inputs.TryGetValue(key, out var value) && value != null)
            {
                var stringValue = value.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(stringValue))
                {
                    Logger.LogDebug("Modbus using upstream dynamic write value: Key={Key}", key);
                    return stringValue;
                }
            }
        }

        return staticValue;
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var forbiddenRawTarget = FindForbiddenRawTargetParameter(@operator);
        if (forbiddenRawTarget != null)
        {
            return ValidationResult.Invalid(
                $"PLC_RAW_TARGET_FORBIDDEN: {forbiddenRawTarget} cannot grant execution authority; use ProfileId and an allow-listed RegisterAddress/FunctionCode binding.");
        }

        var profileId = GetStringParam(@operator, "ProfileId", string.Empty);
        var registerAddress = GetIntParam(@operator, "RegisterAddress", 0);
        var registerCount = GetIntParam(@operator, "RegisterCount", 1);
        var functionCode = GetStringParam(@operator, "FunctionCode", "ReadHolding");
        var protocol = GetStringParam(@operator, "Protocol", "TCP");
        var timeoutMs = GetIntParam(@operator, "TimeoutMs", DefaultOperationTimeoutMs);

        if (registerCount < 1 || registerCount > 125)
        {
            return ValidationResult.Invalid("RegisterCount must be between 1 and 125.");
        }

        if (!protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase) &&
            !protocol.Equals("RTU", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("Protocol must be TCP or RTU.");
        }

        if (timeoutMs < 100 || timeoutMs > 60000)
        {
            return ValidationResult.Invalid("TimeoutMs must be between 100 and 60000.");
        }

        if (!protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("Modbus RTU is not supported by this operator.");
        }

        var resolution = _executionResourceProfileResolver.ResolvePlc(
            profileId,
            new PlcExecutionResourceRequest(
                ExecutionPlcProtocols.ModbusTcp,
                registerAddress.ToString(System.Globalization.CultureInfo.InvariantCulture),
                functionCode,
                functionCode is "WriteSingle" ? 1 : registerCount));
        if (!resolution.Resolved || resolution.Resource == null)
        {
            return ValidationResult.Invalid($"{resolution.Code}: {resolution.Message}");
        }

        return ValidationResult.Valid();
    }

    private static string? FindForbiddenRawTargetParameter(Operator @operator) =>
        @operator.Parameters.FirstOrDefault(parameter =>
            parameter.Name.Equals("Protocol", StringComparison.OrdinalIgnoreCase) ||
            parameter.Name.Equals("IpAddress", StringComparison.OrdinalIgnoreCase) ||
            parameter.Name.Equals("Port", StringComparison.OrdinalIgnoreCase) ||
            parameter.Name.Equals("SlaveId", StringComparison.OrdinalIgnoreCase))?.Name;

    internal sealed record ModbusDispatchRequest(
        string Host,
        int Port,
        int UnitId,
        string FunctionCode,
        int RegisterAddress,
        int RegisterCount,
        string WriteValue,
        int TimeoutMs);

    private async Task<PooledModbusConnectionLease> GetOrCreateConnectionLeaseAsync(
        string ipAddress,
        int port,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var key = BuildConnectionKey(ipAddress, port);
        var lease = await AcquireConnectionLeaseCoreAsync(
            key,
            timeoutMs,
            token => CreateTcpConnectionResourceAsync(ipAddress, port, timeoutMs, token),
            cancellationToken);
        if (lease.IsNewConnection)
        {
            Logger.LogInformation("Modbus connection established: {Key}", key);
        }
        else
        {
            Logger.LogDebug("Reusing Modbus connection: {Key}", key);
        }

        return lease;
    }

    internal static Task<PooledModbusConnectionLease> AcquireConnectionLeaseForTestingAsync(
        string connectionKey,
        Func<CancellationToken, Task<IModbusConnectionResource>> factory,
        CancellationToken cancellationToken = default) =>
        AcquireConnectionLeaseCoreAsync(
            connectionKey,
            DefaultOperationTimeoutMs,
            factory,
            cancellationToken);

    private static async Task<PooledModbusConnectionLease> AcquireConnectionLeaseCoreAsync(
        string connectionKey,
        int timeoutMs,
        Func<CancellationToken, Task<IModbusConnectionResource>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionKey);
        ArgumentNullException.ThrowIfNull(factory);

        var connectionLock = AcquireRefCountedSemaphore(ConnectionLocks, connectionKey);
        var lockAcquired = false;
        var reservationHeld = false;
        var reservationGeneration = 0L;
        IModbusConnectionResource? newResource = null;

        try
        {
            await connectionLock.Semaphore.WaitAsync(cancellationToken);
            lockAcquired = true;

            var immediateDisposals = new List<Task>();
            PooledModbusConnectionLease? existingLease = null;
            lock (PoolSync)
            {
                var nowUtc = PoolTimeProvider.GetUtcNow();
                MaintainPoolUnderLock(nowUtc, immediateDisposals);

                if (ConnectionPool.TryGetValue(connectionKey, out var existingEntry) &&
                    IsResourceConnected(existingEntry.Resource))
                {
                    existingEntry.Resource.ApplyTimeouts(timeoutMs);
                    existingEntry.TryAcquireLease(
                        nowUtc,
                        isNewConnection: false,
                        out existingLease);
                }

                if (existingLease == null)
                {
                    EnsureCapacityForReservationUnderLock(immediateDisposals);
                    if (GetLiveConnectionCountUnderLock() + PendingConnectionCount >= MaxPooledConnections)
                    {
                        throw new InvalidOperationException(
                            $"MODBUS_CONNECTION_POOL_CAPACITY_REACHED: capacity={MaxPooledConnections}.");
                    }

                    PendingConnectionCount++;
                    reservationHeld = true;
                    reservationGeneration = PoolGeneration;
                }
            }

            if (immediateDisposals.Count > 0)
            {
                await Task.WhenAll(immediateDisposals);
            }

            if (existingLease != null)
            {
                return existingLease;
            }

            newResource = await factory(cancellationToken);
            newResource.ApplyTimeouts(timeoutMs);
            if (!IsResourceConnected(newResource))
            {
                throw new InvalidOperationException($"Modbus connection '{connectionKey}' is not connected.");
            }

            PooledModbusConnectionLease? createdLease = null;
            var generationChanged = false;
            lock (PoolSync)
            {
                PendingConnectionCount--;
                reservationHeld = false;
                generationChanged = reservationGeneration != PoolGeneration;
                if (!generationChanged)
                {
                    var entry = new PooledModbusConnectionEntry(
                        connectionKey,
                        newResource,
                        PoolTimeProvider.GetUtcNow());
                    if (!ConnectionPool.TryAdd(connectionKey, entry) ||
                        !entry.TryAcquireLease(
                            PoolTimeProvider.GetUtcNow(),
                            isNewConnection: true,
                            out createdLease))
                    {
                        throw new InvalidOperationException(
                            $"Modbus connection '{connectionKey}' was created through an uncoordinated path.");
                    }

                    Interlocked.Increment(ref CreatedConnectionCount);
                    newResource = null;
                }
            }

            if (generationChanged)
            {
                DisposeUnpooledResource(newResource!);
                newResource = null;
                throw new InvalidOperationException("MODBUS_CONNECTION_POOL_RESET_DURING_CONNECT.");
            }

            return createdLease!;
        }
        finally
        {
            if (newResource != null)
            {
                DisposeUnpooledResource(newResource);
            }

            if (reservationHeld)
            {
                lock (PoolSync)
                {
                    PendingConnectionCount--;
                }
            }

            if (lockAcquired)
            {
                connectionLock.Semaphore.Release();
            }

            ReleaseRefCountedSemaphore(ConnectionLocks, connectionKey, connectionLock);
        }
    }

    private static async Task<IModbusConnectionResource> CreateTcpConnectionResourceAsync(
        string ipAddress,
        int port,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var client = new TcpClient
        {
            NoDelay = true,
            ReceiveTimeout = timeoutMs,
            SendTimeout = timeoutMs
        };
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(timeoutMs);

        try
        {
            await client.ConnectAsync(ipAddress, port, connectTimeout.Token);
            var master = ModbusFactory.CreateMaster(client);
            return new TcpModbusConnectionResource(client, master);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static bool IsResourceConnected(IModbusConnectionResource resource)
    {
        try
        {
            return resource.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyDictionary<string, bool> GetConnectionStateSnapshot()
    {
        lock (PoolSync)
        {
            return ConnectionPool.ToDictionary(
                pair => pair.Key,
                pair => IsResourceConnected(pair.Value.Resource),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public static ModbusConnectionPoolSnapshot GetConnectionPoolSnapshot()
    {
        lock (PoolSync)
        {
            var retiring = RetiringConnections.Keys.ToArray();
            return new ModbusConnectionPoolSnapshot(
                Capacity: MaxPooledConnections,
                PooledCount: ConnectionPool.Count,
                ConnectedCount: ConnectionPool.Values.Count(entry => IsResourceConnected(entry.Resource)),
                ActiveLeaseCount: ConnectionPool.Values.Sum(entry => entry.LeaseCount)
                    + retiring.Sum(entry => entry.LeaseCount),
                RetiringCount: retiring.Length,
                PendingConnectionCount: PendingConnectionCount,
                ConnectionKeyLockCount: ConnectionLocks.Count,
                ConnectionKeyLockReferenceCount: ConnectionLocks.Values.Sum(entry => entry.ReferenceCount),
                OperationLockCount: OperationLocks.Count,
                OperationLockReferenceCount: OperationLocks.Values.Sum(entry => entry.ReferenceCount),
                CreatedConnectionCount: Interlocked.Read(ref CreatedConnectionCount),
                DisposedConnectionCount: Interlocked.Read(ref DisposedConnectionCount),
                ClearRetirementCount: Interlocked.Read(ref ClearRetirementCount),
                IdleEvictionCount: Interlocked.Read(ref IdleEvictionCount),
                CapacityEvictionCount: Interlocked.Read(ref CapacityEvictionCount),
                DisconnectedRemovalCount: Interlocked.Read(ref DisconnectedRemovalCount));
        }
    }

    public static void ClearConnectionPool()
    {
        RetireAllConnections();
    }

    public static async Task ClearConnectionPoolAsync()
    {
        var snapshot = RetireAllConnections();
        await Task.WhenAll(snapshot.Select(entry => entry.DisposalCompleted));
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
        lock (PoolSync)
        {
            if (PendingConnectionCount != 0 || !RetiringConnections.IsEmpty)
            {
                throw new InvalidOperationException("Modbus pool cannot be reconfigured while resources are active.");
            }

            MaxPooledConnections = capacity;
            MaxIdleConnectionAge = maxIdleConnectionAge;
            PoolTimeProvider = timeProvider;
            ResetPoolCountersUnderLock();
        }
    }

    internal static async Task ResetConnectionPoolPolicyForTestingAsync()
    {
        await ClearConnectionPoolAsync();
        lock (PoolSync)
        {
            MaxPooledConnections = DefaultMaxPooledConnections;
            MaxIdleConnectionAge = DefaultMaxIdleConnectionAge;
            PoolTimeProvider = TimeProvider.System;
            ResetPoolCountersUnderLock();
        }
    }

    internal static Task RunConnectionPoolMaintenanceForTestingAsync()
    {
        List<Task> immediateDisposals = [];
        lock (PoolSync)
        {
            MaintainPoolUnderLock(PoolTimeProvider.GetUtcNow(), immediateDisposals);
        }

        return immediateDisposals.Count == 0
            ? Task.CompletedTask
            : Task.WhenAll(immediateDisposals);
    }

    private async Task<(string response, bool status)> ExecuteTcpModbusAsync(
        string ipAddress,
        int port,
        int slaveId,
        string functionCode,
        int registerAddress,
        int registerCount,
        string writeValue,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var key = BuildConnectionKey(ipAddress, port);
        var operationLock = AcquireRefCountedSemaphore(OperationLocks, key);
        using var operationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationTimeout.CancelAfter(timeoutMs);
        var lockAcquired = false;
        PooledModbusConnectionLease? connectionLease = null;

        try
        {
            await operationLock.Semaphore.WaitAsync(operationTimeout.Token);
            lockAcquired = true;
            connectionLease = await GetOrCreateConnectionLeaseAsync(
                ipAddress,
                port,
                timeoutMs,
                operationTimeout.Token);
            return ExecuteModbusFunction(
                connectionLease.Master,
                slaveId,
                functionCode,
                registerAddress,
                registerCount,
                writeValue);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ("Communication was cancelled.", false);
        }
        catch (OperationCanceledException)
        {
            if (lockAcquired)
            {
                RetireConnection(key, ConnectionRetirementReason.Disconnected);
            }

            return ("Communication timed out.", false);
        }
        catch (IOException ex)
        {
            RetireConnection(key, ConnectionRetirementReason.Disconnected);
            Logger.LogError(ex, "Modbus IO communication failed: {Key}", key);
            return ($"Communication failed: {ex.Message}", false);
        }
        catch (SocketException ex)
        {
            RetireConnection(key, ConnectionRetirementReason.Disconnected);
            Logger.LogError(ex, "Modbus socket communication failed: {Key}", key);
            return ($"Communication failed: {ex.Message}", false);
        }
        catch (TimeoutException ex)
        {
            RetireConnection(key, ConnectionRetirementReason.Disconnected);
            Logger.LogError(ex, "Modbus communication timed out: {Key}", key);
            return ($"Communication timed out: {ex.Message}", false);
        }
        catch (Exception ex)
        {
            RetireConnection(key, ConnectionRetirementReason.Disconnected);
            Logger.LogError(ex, "Modbus communication failed: {Key}", key);
            return ($"Communication failed: {ex.Message}", false);
        }
        finally
        {
            if (connectionLease != null)
            {
                await connectionLease.DisposeAsync();
            }

            if (lockAcquired)
            {
                operationLock.Semaphore.Release();
            }

            ReleaseRefCountedSemaphore(OperationLocks, key, operationLock);
        }
    }

    private static (string response, bool status) ExecuteModbusFunction(
        IModbusMaster master,
        int slaveId,
        string functionCode,
        int registerAddress,
        int registerCount,
        string writeValue)
    {
        switch (functionCode)
        {
            case "ReadCoils":
                var coils = master.ReadCoils((byte)slaveId, (ushort)registerAddress, (ushort)registerCount);
                return (string.Join(", ", coils), true);

            case "ReadHolding":
                var registers = master.ReadHoldingRegisters((byte)slaveId, (ushort)registerAddress, (ushort)registerCount);
                return (string.Join(", ", registers), true);

            case "WriteSingle":
                if (!ushort.TryParse(writeValue, out var singleValue))
                {
                    return ("WriteValue must be a valid unsigned 16-bit integer.", false);
                }

                master.WriteSingleRegister((byte)slaveId, (ushort)registerAddress, singleValue);
                return ($"Write succeeded: {singleValue}", true);

            case "WriteMultiple":
                if (!TryParseRegisterValues(writeValue, out var values))
                {
                    return ("WriteValue must be a comma-separated list of unsigned 16-bit integers.", false);
                }

                master.WriteMultipleRegisters((byte)slaveId, (ushort)registerAddress, values);
                return ($"Write succeeded: {values.Length} registers", true);

            default:
                return ($"Unknown function code: {functionCode}", false);
        }
    }

    private static bool TryParseRegisterValues(string writeValue, out ushort[] values)
    {
        var parsed = new List<ushort>();
        foreach (var part in writeValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!ushort.TryParse(part, out var value))
            {
                values = [];
                return false;
            }

            parsed.Add(value);
        }

        values = parsed.ToArray();
        return values.Length > 0;
    }

    private static string BuildConnectionKey(string ipAddress, int port)
    {
        return $"{ipAddress}:{port}";
    }

    private static PooledModbusConnectionEntry[] RetireAllConnections()
    {
        lock (PoolSync)
        {
            PoolGeneration++;
            var snapshot = ConnectionPool.Values.ToArray();
            ConnectionPool.Clear();
            foreach (var entry in snapshot)
            {
                RetiringConnections.TryAdd(entry, 0);
                Interlocked.Increment(ref ClearRetirementCount);
                entry.Retire();
            }

            return snapshot;
        }
    }

    private static void ResetPoolCountersUnderLock()
    {
        CreatedConnectionCount = 0;
        DisposedConnectionCount = 0;
        ClearRetirementCount = 0;
        IdleEvictionCount = 0;
        CapacityEvictionCount = 0;
        DisconnectedRemovalCount = 0;
    }

    private static int GetLiveConnectionCountUnderLock() =>
        ConnectionPool.Count + RetiringConnections.Count;

    private static void MaintainPoolUnderLock(
        DateTimeOffset nowUtc,
        List<Task> immediateDisposals)
    {
        foreach (var (key, entry) in ConnectionPool.ToArray())
        {
            if (!IsResourceConnected(entry.Resource))
            {
                RemoveEntryUnderLock(
                    key,
                    entry,
                    ConnectionRetirementReason.Disconnected,
                    immediateDisposals);
            }
            else if (entry.IsIdle(nowUtc, MaxIdleConnectionAge))
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
        while (GetLiveConnectionCountUnderLock() + PendingConnectionCount >= MaxPooledConnections)
        {
            var candidate = ConnectionPool
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
        PooledModbusConnectionEntry entry,
        ConnectionRetirementReason reason,
        List<Task> immediateDisposals)
    {
        if (!ConnectionPool.TryGetValue(connectionKey, out var current) ||
            !ReferenceEquals(current, entry) ||
            !ConnectionPool.Remove(connectionKey))
        {
            return false;
        }

        RetiringConnections.TryAdd(entry, 0);
        if (entry.Retire())
        {
            immediateDisposals.Add(entry.DisposalCompleted);
        }

        IncrementRetirementCounter(reason);
        return true;
    }

    private static void RetireConnection(string connectionKey, ConnectionRetirementReason reason)
    {
        List<Task> immediateDisposals = [];
        lock (PoolSync)
        {
            if (ConnectionPool.TryGetValue(connectionKey, out var entry))
            {
                RemoveEntryUnderLock(connectionKey, entry, reason, immediateDisposals);
            }
        }
    }

    private static async ValueTask ReleaseConnectionLeaseAsync(PooledModbusConnectionEntry entry)
    {
        var disposeStarted = entry.ReleaseLease(PoolTimeProvider.GetUtcNow());
        if (disposeStarted)
        {
            await entry.DisposalCompleted;
            return;
        }

        if (!entry.IsRetired && !IsResourceConnected(entry.Resource))
        {
            List<Task> immediateDisposals = [];
            lock (PoolSync)
            {
                RemoveEntryUnderLock(
                    entry.ConnectionKey,
                    entry,
                    ConnectionRetirementReason.Disconnected,
                    immediateDisposals);
            }

            if (immediateDisposals.Count > 0)
            {
                await Task.WhenAll(immediateDisposals);
            }
        }
    }

    private static void IncrementRetirementCounter(ConnectionRetirementReason reason)
    {
        switch (reason)
        {
            case ConnectionRetirementReason.Clear:
                Interlocked.Increment(ref ClearRetirementCount);
                break;
            case ConnectionRetirementReason.Idle:
                Interlocked.Increment(ref IdleEvictionCount);
                break;
            case ConnectionRetirementReason.Capacity:
                Interlocked.Increment(ref CapacityEvictionCount);
                break;
            case ConnectionRetirementReason.Disconnected:
                Interlocked.Increment(ref DisconnectedRemovalCount);
                break;
        }
    }

    private static void DisposeUnpooledResource(IModbusConnectionResource resource)
    {
        try
        {
            resource.Dispose();
        }
        catch
        {
            // A failed or invalidated reservation is unreachable from the pool.
        }
    }

    private static void OnPooledEntryDisposed(PooledModbusConnectionEntry entry)
    {
        RetiringConnections.TryRemove(entry, out _);
        Interlocked.Increment(ref DisposedConnectionCount);
    }

    private enum ConnectionRetirementReason
    {
        Clear,
        Idle,
        Capacity,
        Disconnected
    }

    internal sealed class PooledModbusConnectionEntry
    {
        private readonly object _sync = new();
        private readonly TaskCompletionSource _disposalCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DateTimeOffset _lastUsedUtc;
        private int _leaseCount;
        private bool _retired;
        private bool _disposeStarted;

        public PooledModbusConnectionEntry(
            string connectionKey,
            IModbusConnectionResource resource,
            DateTimeOffset nowUtc)
        {
            ConnectionKey = connectionKey;
            Resource = resource;
            _lastUsedUtc = nowUtc;
        }

        public string ConnectionKey { get; }

        public IModbusConnectionResource Resource { get; }

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
            out PooledModbusConnectionLease? lease)
        {
            lock (_sync)
            {
                if (_retired)
                {
                    lease = null;
                    return false;
                }

                _leaseCount++;
                _lastUsedUtc = nowUtc;
                lease = new PooledModbusConnectionLease(this, isNewConnection);
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
                _retired = true;
                if (_leaseCount == 0 && !_disposeStarted)
                {
                    _disposeStarted = true;
                    disposeNow = true;
                }
            }

            if (disposeNow)
            {
                DisposeResource();
            }

            return disposeNow;
        }

        public bool ReleaseLease(DateTimeOffset nowUtc)
        {
            var disposeNow = false;
            lock (_sync)
            {
                if (_leaseCount <= 0)
                {
                    throw new InvalidOperationException("Modbus connection lease count underflow.");
                }

                _leaseCount--;
                _lastUsedUtc = nowUtc;
                if (_retired && _leaseCount == 0 && !_disposeStarted)
                {
                    _disposeStarted = true;
                    disposeNow = true;
                }
            }

            if (disposeNow)
            {
                DisposeResource();
            }

            return disposeNow;
        }

        private void DisposeResource()
        {
            try
            {
                Resource.Dispose();
            }
            catch
            {
                // Retirement is complete once the resource is unreachable, even if close fails.
            }
            finally
            {
                OnPooledEntryDisposed(this);
                _disposalCompleted.TrySetResult();
            }
        }
    }

    internal sealed class PooledModbusConnectionLease : IDisposable, IAsyncDisposable
    {
        private PooledModbusConnectionEntry? _entry;

        internal PooledModbusConnectionLease(PooledModbusConnectionEntry entry, bool isNewConnection)
        {
            _entry = entry;
            Master = entry.Resource.Master;
            ConnectionKey = entry.ConnectionKey;
            IsNewConnection = isNewConnection;
        }

        public IModbusMaster Master { get; }

        public string ConnectionKey { get; }

        public bool IsNewConnection { get; }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry != null)
            {
                await ReleaseConnectionLeaseAsync(entry);
            }
        }
    }

    private sealed class TcpModbusConnectionResource : IModbusConnectionResource
    {
        private readonly TcpClient _client;
        private int _disposed;

        public TcpModbusConnectionResource(TcpClient client, IModbusMaster master)
        {
            _client = client;
            Master = master;
        }

        public IModbusMaster Master { get; }

        public bool IsConnected =>
            Volatile.Read(ref _disposed) == 0 &&
            _client.Connected &&
            !(_client.Client.Poll(1, SelectMode.SelectRead) && _client.Client.Available == 0);

        public void ApplyTimeouts(int timeoutMs)
        {
            _client.ReceiveTimeout = timeoutMs;
            _client.SendTimeout = timeoutMs;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                Master.Dispose();
            }
            catch
            {
            }

            try
            {
                _client.Close();
                _client.Dispose();
            }
            catch
            {
            }
        }
    }

    private sealed class RefCountedSemaphore : IDisposable
    {
        private readonly object _sync = new();
        private int _refCount;
        private bool _removed;

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
                if (_removed)
                {
                    return false;
                }

                _refCount++;
                return true;
            }
        }

        public bool ReleaseRefAndMarkRemovedIfUnused()
        {
            lock (_sync)
            {
                if (_refCount <= 0)
                {
                    throw new InvalidOperationException("Modbus keyed lock reference count underflow.");
                }

                _refCount--;
                if (_refCount != 0)
                {
                    return false;
                }

                _removed = true;
                return true;
            }
        }

        public void Dispose()
        {
            Semaphore.Dispose();
        }
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

            if (entry.ReleaseRefAndMarkRemovedIfUnused())
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
        if (!entry.ReleaseRefAndMarkRemovedIfUnused())
        {
            return;
        }

        if (dictionary.TryRemove(new KeyValuePair<string, RefCountedSemaphore>(key, entry)))
        {
            entry.Dispose();
        }
    }
}
