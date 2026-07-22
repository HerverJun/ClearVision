using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Infrastructure.Communication.Gr;
using Microsoft.Extensions.Logging;
using NModbus;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "Modbus TCP通信",
    Description = "通过 Modbus TCP 读写线圈和保持寄存器；当前算子不执行 Modbus RTU 通信。",
    CategoryId = OperatorCategoryId.Communication,
    IconName = "modbus",
    Keywords = new[] { "Modbus", "PLC", "Communication", "Register", "RTU", "TCP", "Industrial", "Modbus通信", "Modbus Communication" },
    Version = "1.1.0"
)]
[OperatorParameterRule("IpAddress", RequiredPolicy = OperatorParameterRequiredPolicy.Required, ResourceKind = OperatorResourceKind.PlcEndpoint, ReasonCode = "MODBUS_TCP_ENDPOINT_REQUIRED")]
[OperatorParameterRule("RegisterAddress", RequiredPolicy = OperatorParameterRequiredPolicy.Required, ResourceKind = OperatorResourceKind.PlcAddress, ReasonCode = "MODBUS_REGISTER_ADDRESS_REQUIRED")]
[OperatorParameterRule("RegisterCount", EnabledWhenAny = new[] { "FunctionCode==ReadCoils", "FunctionCode==ReadHolding" }, HiddenWhenAny = new[] { "FunctionCode==WriteSingle", "FunctionCode==WriteMultiple" }, IgnoredWhenAny = new[] { "FunctionCode==WriteSingle", "FunctionCode==WriteMultiple" }, ReasonCode = "MODBUS_REGISTER_COUNT_ONLY_FOR_READ")]
[OperatorParameterRule("WriteValue", RequiredWhenAny = new[] { "FunctionCode==WriteSingle", "FunctionCode==WriteMultiple" }, EnabledWhenAny = new[] { "FunctionCode==WriteSingle", "FunctionCode==WriteMultiple" }, HiddenWhenAny = new[] { "FunctionCode==ReadCoils", "FunctionCode==ReadHolding" }, IgnoredWhenAny = new[] { "FunctionCode==ReadCoils", "FunctionCode==ReadHolding" }, ReasonCode = "MODBUS_WRITE_VALUE_ONLY_FOR_WRITE")]
[InputPort("Data", "Data", PortDataType.Any, IsRequired = false)]
[OutputPort("Response", "Response", PortDataType.String)]
[OutputPort("Status", "Status", PortDataType.Boolean)]
[OutputPort("Values", "Values", PortDataType.Any)]
[OutputPort("Registers", "Registers", PortDataType.Any)]
[OutputPort("StartAddress", "Start Address", PortDataType.Integer)]
[OutputPort("Count", "Count", PortDataType.Integer)]
[OutputPort("Operation", "Operation", PortDataType.String)]
[OutputPort("LatencyMs", "Latency (ms)", PortDataType.Integer)]
[OutputPort("WriteReceipt", "Write Receipt", PortDataType.Any)]
[OperatorParam("Protocol", "Protocol", "enum", Description = "当前仅支持 TCP；RTU 选项用于旧流程兼容，执行时返回不支持。", DefaultValue = "TCP", Options = new[] { "TCP|TCP", "RTU|RTU" })]
[OperatorParam("ProfileId", "Device Profile", "string", Description = "Optional local Modbus device profile ID.", DefaultValue = "")]
[OperatorParam("TemplateId", "Template ID", "string", DefaultValue = "")]
[OperatorParam("TemplateVersion", "Template Version", "string", DefaultValue = "")]
[OperatorParam("TemplateHash", "Template Hash", "string", DefaultValue = "")]
[OperatorParam("IpAddress", "IP Address", "string", DefaultValue = "192.168.1.1")]
[OperatorParam("Port", "Port", "int", DefaultValue = 502, Min = 1, Max = 65535)]
[OperatorParam("SlaveId", "Slave ID", "int", DefaultValue = 1, Min = 1, Max = 255)]
[OperatorParam("RegisterAddress", "Register Address", "int", DefaultValue = 0)]
[OperatorParam("RegisterCount", "Register Count", "int", DefaultValue = 1, Min = 1, Max = 125)]
[OperatorParam("FunctionCode", "Function Code", "enum", DefaultValue = "ReadHolding", Options = new[] { "ReadCoils|Read Coils", "ReadHolding|Read Holding Registers", "WriteSingle|Write Single Register", "WriteMultiple|Write Multiple Registers" })]
[OperatorParam("WriteValue", "Write Value", "string", DefaultValue = "")]
[OperatorParam("TimeoutMs", "Timeout (ms)", "int", DefaultValue = 5000, Min = 100, Max = 60000)]
public class ModbusCommunicationOperator : OperatorBase
{
    private const int DefaultOperationTimeoutMs = 5000;
    private const int MaxPooledConnections = 32;
    private static readonly TimeSpan MaxIdleConnectionAge = TimeSpan.FromMinutes(10);

    private static readonly ConcurrentDictionary<string, TcpClient> ConnectionPool = new();
    private static readonly ConcurrentDictionary<string, RefCountedSemaphore> ConnectionLocks = new();
    private static readonly ConcurrentDictionary<string, RefCountedSemaphore> OperationLocks = new();
    private static readonly ConcurrentDictionary<string, IModbusMaster> MasterPool = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> ConnectionLastUsed = new();
    private static readonly ConcurrentDictionary<string, int> ActiveOperations = new();
    private static readonly IModbusFactory ModbusFactory = new ModbusFactory();
    private readonly JsonCommunicationProfileStore? _profileStore;

    public ModbusCommunicationOperator(
        ILogger<ModbusCommunicationOperator> logger,
        JsonCommunicationProfileStore? profileStore = null) : base(logger)
    {
        _profileStore = profileStore;
    }

    public override OperatorType OperatorType => OperatorType.ModbusCommunication;

    protected override async Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var profileId = GetStringParam(@operator, "ProfileId", string.Empty);
        var profile = string.IsNullOrWhiteSpace(profileId) ? null : _profileStore?.Get(profileId);
        if (!string.IsNullOrWhiteSpace(profileId) && profile == null)
        {
            return OperatorExecutionOutput.Failure($"Modbus profile '{profileId}' was not found.");
        }

        var protocol = GetStringParam(@operator, "Protocol", "TCP");
        var ipAddress = profile?.Host ?? GetStringParam(@operator, "IpAddress", "192.168.1.1");
        var port = profile?.Port ?? GetIntParam(@operator, "Port", 502, 1, 65535);
        var slaveId = profile?.UnitId ?? GetIntParam(@operator, "SlaveId", 1, 1, 255);
        var registerAddress = GetIntParam(@operator, "RegisterAddress", 0);
        var registerCount = GetIntParam(@operator, "RegisterCount", 1, 1, 125);
        var functionCode = GetStringParam(@operator, "FunctionCode", "ReadHolding");
        // 写操作优先使用上游连线到 "Data" 端口的动态值，否则回退参数面板中的静态 WriteValue。
        var writeValue = ResolveWriteValue(@operator, inputs);
        var timeoutMs = GetIntParam(@operator, "TimeoutMs", DefaultOperationTimeoutMs, 100, 60000);

        if (profile?.ReadOnly == true && functionCode is "WriteSingle" or "WriteMultiple")
        {
            return OperatorExecutionOutput.Failure($"Modbus profile '{profile.Id}' is read-only.");
        }

        if (!protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase))
        {
            return OperatorExecutionOutput.Failure("Modbus RTU requires serial-port lifecycle configuration and is not supported by this package operator.");
        }

        var stopwatch = Stopwatch.StartNew();
        var operationResult = await ExecuteTcpModbusAsync(
            ipAddress,
            port,
            slaveId,
            functionCode,
            registerAddress,
            registerCount,
            writeValue,
            timeoutMs,
            cancellationToken);
        stopwatch.Stop();

        if (!operationResult.IsSuccess)
        {
            return OperatorExecutionOutput.Failure(operationResult.Response);
        }

        return OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["Response"] = operationResult.Response,
            ["Status"] = true,
            ["Protocol"] = protocol,
            ["FunctionCode"] = functionCode,
            ["SlaveId"] = slaveId,
            ["Values"] = operationResult.Values,
            ["Registers"] = operationResult.Registers,
            ["StartAddress"] = registerAddress,
            ["Count"] = operationResult.Count,
            ["Operation"] = functionCode,
            ["LatencyMs"] = stopwatch.ElapsedMilliseconds,
            ["WriteReceipt"] = (object?)operationResult.WriteReceipt ?? new Dictionary<string, object>(),
            ["ProfileId"] = profile?.Id ?? profileId,
            ["TemplateId"] = profile?.TemplateId ?? GetStringParam(@operator, "TemplateId", string.Empty),
            ["TemplateVersion"] = profile?.TemplateVersion ?? GetStringParam(@operator, "TemplateVersion", string.Empty),
            ["TemplateHash"] = profile?.TemplateHash ?? GetStringParam(@operator, "TemplateHash", string.Empty)
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
        var port = GetIntParam(@operator, "Port", 502);
        var slaveId = GetIntParam(@operator, "SlaveId", 1);
        var registerCount = GetIntParam(@operator, "RegisterCount", 1);
        var protocol = GetStringParam(@operator, "Protocol", "TCP");
        var timeoutMs = GetIntParam(@operator, "TimeoutMs", DefaultOperationTimeoutMs);

        if (port < 1 || port > 65535)
        {
            return ValidationResult.Invalid("Port must be between 1 and 65535.");
        }

        if (slaveId < 1 || slaveId > 255)
        {
            return ValidationResult.Invalid("SlaveId must be between 1 and 255.");
        }

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

        return ValidationResult.Valid();
    }

    private async Task<IModbusMaster> GetOrCreateConnectionAsync(
        string ipAddress,
        int port,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var key = BuildConnectionKey(ipAddress, port);
        var connectionLock = AcquireRefCountedSemaphore(ConnectionLocks, key);
        var lockAcquired = false;

        try
        {
            await connectionLock.Semaphore.WaitAsync(cancellationToken);
            lockAcquired = true;

            CleanupIdleConnections(DateTimeOffset.UtcNow);

            if (MasterPool.TryGetValue(key, out var existingMaster) &&
                ConnectionPool.TryGetValue(key, out var existingClient))
            {
                if (IsConnectionAlive(existingClient))
                {
                    ApplyClientTimeouts(existingClient, timeoutMs);
                    TouchConnection(key);
                    Logger.LogDebug("Reusing Modbus connection: {Key}", key);
                    return existingMaster;
                }

                PurgeConnection(key, force: true);
            }

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
                ConnectionPool[key] = client;
                MasterPool[key] = master;
                TouchConnection(key);
                TrimConnectionPoolIfNeeded(key);

                Logger.LogInformation("Modbus connection established: {Key}", key);
                return master;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        finally
        {
            if (lockAcquired)
            {
                connectionLock.Semaphore.Release();
            }

            ReleaseRefCountedSemaphore(ConnectionLocks, key, connectionLock);
        }
    }

    private static bool IsConnectionAlive(TcpClient client)
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

    public static IReadOnlyDictionary<string, bool> GetConnectionStateSnapshot()
    {
        var snapshot = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, client) in ConnectionPool)
        {
            snapshot[key] = IsConnectionAlive(client);
        }

        return snapshot;
    }

    public static void ClearConnectionPool()
    {
        foreach (var (_, master) in MasterPool)
        {
            try
            {
                master.Dispose();
            }
            catch
            {
                // Ignore dispose failures while resetting local station settings.
            }
        }

        foreach (var (_, client) in ConnectionPool)
        {
            try
            {
                client.Close();
                client.Dispose();
            }
            catch
            {
                // Ignore dispose failures while resetting local station settings.
            }
        }

        MasterPool.Clear();
        ConnectionPool.Clear();
        ConnectionLastUsed.Clear();
    }

    private static void ApplyClientTimeouts(TcpClient client, int timeoutMs)
    {
        client.ReceiveTimeout = timeoutMs;
        client.SendTimeout = timeoutMs;
    }

    private async Task<ModbusOperationResult> ExecuteTcpModbusAsync(
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
        var operationTracked = false;

        try
        {
            await operationLock.Semaphore.WaitAsync(operationTimeout.Token);
            lockAcquired = true;
            IncrementActiveOperations(key);
            operationTracked = true;

            var master = await GetOrCreateConnectionAsync(ipAddress, port, timeoutMs, operationTimeout.Token);
            return ExecuteModbusFunction(master, slaveId, functionCode, registerAddress, registerCount, writeValue);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ModbusOperationResult.Failure("Communication was cancelled.");
        }
        catch (OperationCanceledException)
        {
            if (lockAcquired)
            {
                PurgeConnection(key, force: true);
            }

            return ModbusOperationResult.Failure("Communication timed out.");
        }
        catch (IOException ex)
        {
            PurgeConnection(key, force: true);
            Logger.LogError(ex, "Modbus IO communication failed: {Key}", key);
            return ModbusOperationResult.Failure($"Communication failed: {ex.Message}");
        }
        catch (SocketException ex)
        {
            PurgeConnection(key, force: true);
            Logger.LogError(ex, "Modbus socket communication failed: {Key}", key);
            return ModbusOperationResult.Failure($"Communication failed: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            PurgeConnection(key, force: true);
            Logger.LogError(ex, "Modbus communication timed out: {Key}", key);
            return ModbusOperationResult.Failure($"Communication timed out: {ex.Message}");
        }
        catch (Exception ex)
        {
            PurgeConnection(key, force: true);
            Logger.LogError(ex, "Modbus communication failed: {Key}", key);
            return ModbusOperationResult.Failure($"Communication failed: {ex.Message}");
        }
        finally
        {
            if (operationTracked)
            {
                if (ConnectionPool.ContainsKey(key))
                {
                    TouchConnection(key);
                }
                else
                {
                    ConnectionLastUsed.TryRemove(key, out _);
                }

                DecrementActiveOperations(key);
            }

            if (lockAcquired)
            {
                operationLock.Semaphore.Release();
            }

            ReleaseRefCountedSemaphore(OperationLocks, key, operationLock);
        }
    }

    private static ModbusOperationResult ExecuteModbusFunction(
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
                return ModbusOperationResult.Read(
                    string.Join(", ", coils),
                    coils.Cast<object>().ToArray(),
                    []);

            case "ReadHolding":
                var registers = master.ReadHoldingRegisters((byte)slaveId, (ushort)registerAddress, (ushort)registerCount);
                var registerValues = registers
                    .Select((value, index) => new ModbusRegisterValue(registerAddress + index, value))
                    .ToArray();
                return ModbusOperationResult.Read(
                    string.Join(", ", registers),
                    registers.Cast<object>().ToArray(),
                    registerValues);

            case "WriteSingle":
                if (!ushort.TryParse(writeValue, out var singleValue))
                {
                    return ModbusOperationResult.Failure("WriteValue must be a valid unsigned 16-bit integer.");
                }

                master.WriteSingleRegister((byte)slaveId, (ushort)registerAddress, singleValue);
                return ModbusOperationResult.Write(
                    $"Write succeeded: {singleValue}",
                    [singleValue],
                    new ModbusWriteReceipt(registerAddress, 1, [singleValue]));

            case "WriteMultiple":
                if (!TryParseRegisterValues(writeValue, out var values))
                {
                    return ModbusOperationResult.Failure("WriteValue must be a comma-separated list of unsigned 16-bit integers.");
                }

                master.WriteMultipleRegisters((byte)slaveId, (ushort)registerAddress, values);
                return ModbusOperationResult.Write(
                    $"Write succeeded: {values.Length} registers",
                    values.Cast<object>().ToArray(),
                    new ModbusWriteReceipt(registerAddress, values.Length, values));

            default:
                return ModbusOperationResult.Failure($"Unknown function code: {functionCode}");
        }
    }

    private sealed record ModbusRegisterValue(int Address, ushort Value);

    private sealed record ModbusWriteReceipt(int StartAddress, int Count, IReadOnlyList<ushort> Values);

    private sealed record ModbusOperationResult(
        bool IsSuccess,
        string Response,
        IReadOnlyList<object> Values,
        IReadOnlyList<ModbusRegisterValue> Registers,
        int Count,
        ModbusWriteReceipt? WriteReceipt)
    {
        public static ModbusOperationResult Failure(string response) =>
            new(false, response, [], [], 0, null);

        public static ModbusOperationResult Read(
            string response,
            IReadOnlyList<object> values,
            IReadOnlyList<ModbusRegisterValue> registers) =>
            new(true, response, values, registers, values.Count, null);

        public static ModbusOperationResult Write(
            string response,
            IReadOnlyList<object> values,
            ModbusWriteReceipt receipt) =>
            new(true, response, values, [], receipt.Count, receipt);
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

    private void PurgeConnection(string key, bool force)
    {
        if (!force && ActiveOperations.TryGetValue(key, out var activeCount) && activeCount > 0)
        {
            return;
        }

        if (MasterPool.TryRemove(key, out var master))
        {
            try
            { master.Dispose(); }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Modbus master release failed: {Key}", key);
            }
        }

        if (ConnectionPool.TryRemove(key, out var client))
        {
            try
            {
                client.Close();
                client.Dispose();
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Modbus TcpClient release failed: {Key}", key);
            }
        }

        ConnectionLastUsed.TryRemove(key, out _);
    }

    private static string BuildConnectionKey(string ipAddress, int port)
    {
        return $"{ipAddress}:{port}";
    }

    private static void TouchConnection(string key)
    {
        ConnectionLastUsed[key] = DateTimeOffset.UtcNow;
    }

    private void CleanupIdleConnections(DateTimeOffset now)
    {
        foreach (var entry in ConnectionLastUsed)
        {
            if (now - entry.Value > MaxIdleConnectionAge)
            {
                PurgeConnection(entry.Key, force: false);
            }
        }
    }

    private void TrimConnectionPoolIfNeeded(string protectedKey)
    {
        if (ConnectionPool.Count <= MaxPooledConnections)
        {
            return;
        }

        foreach (var candidate in ConnectionLastUsed.OrderBy(entry => entry.Value))
        {
            if (ConnectionPool.Count <= MaxPooledConnections)
            {
                break;
            }

            if (candidate.Key.Equals(protectedKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            PurgeConnection(candidate.Key, force: false);
        }
    }

    private static void IncrementActiveOperations(string key)
    {
        ActiveOperations.AddOrUpdate(key, 1, static (_, count) => count + 1);
    }

    private static void DecrementActiveOperations(string key)
    {
        ActiveOperations.AddOrUpdate(
            key,
            0,
            static (_, count) => Math.Max(0, count - 1));

        if (ActiveOperations.TryGetValue(key, out var count) && count == 0)
        {
            ActiveOperations.TryRemove(new KeyValuePair<string, int>(key, 0));
        }
    }

    private sealed class RefCountedSemaphore : IDisposable
    {
        private readonly object _sync = new();
        private int _refCount;
        private bool _removed;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

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
