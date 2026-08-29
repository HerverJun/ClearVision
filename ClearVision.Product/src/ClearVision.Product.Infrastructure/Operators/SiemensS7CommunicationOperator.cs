// SiemensS7CommunicationOperator.cs
// 解析写入值：优先从上游输入获取，否则使用参数面板静态值
// 作者：蘅芜君

using ClearVision.PlcComm;
using ClearVision.PlcComm.Interfaces;
using ClearVision.PlcComm.Siemens;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// 西门子S7通信算子
/// 支持S7-200/300/400/1200/1500系列PLC读写操作
/// </summary>
[OperatorMeta(
    DisplayName = "西门子S7通信",
    Description = "西门子S7系列PLC读写通信（S7-200/300/400/1200/1500）",
    CategoryId = OperatorCategoryId.Communication,
    IconName = "s7",
    Version = "1.0.1"
)]
[OperatorParameterRule("IpAddress", RequiredWhenAll = new[] { "UseGlobalFallback==false" }, ResourceKind = OperatorResourceKind.PlcEndpoint, ReasonCode = "SIEMENS_OPERATOR_IP_REQUIRED_WITHOUT_GLOBAL_FALLBACK")]
[OperatorParameterRule("Port", RequiredWhenAll = new[] { "UseGlobalFallback==false" }, ReasonCode = "SIEMENS_OPERATOR_PORT_REQUIRED_WITHOUT_GLOBAL_FALLBACK")]
[OperatorParameterRule("Address", RequiredPolicy = OperatorParameterRequiredPolicy.Required, ResourceKind = OperatorResourceKind.PlcAddress, ReasonCode = "SIEMENS_PLC_ADDRESS_REQUIRED")]
[OperatorParameterRule("WriteValue", RequiredPolicy = OperatorParameterRequiredPolicy.Optional, EnabledWhenAll = new[] { "Operation==Write" }, HiddenWhenAll = new[] { "Operation!=Write" }, IgnoredWhenAll = new[] { "Operation!=Write" }, ReasonCode = "SIEMENS_WRITE_VALUE_ONLY_FOR_WRITE")]
[OperatorParameterRule("PollingMode", EnabledWhenAll = new[] { "Operation==Read" }, HiddenWhenAll = new[] { "Operation!=Read" }, IgnoredWhenAll = new[] { "Operation!=Read" }, ReasonCode = "SIEMENS_POLLING_ONLY_FOR_READ")]
[OperatorParameterRule("PollingCondition", EnabledWhenAll = new[] { "Operation==Read", "PollingMode==WaitForValue" }, HiddenWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, IgnoredWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, ReasonCode = "SIEMENS_POLLING_CONDITION_ONLY_WHEN_WAITING")]
[OperatorParameterRule("PollingValue", EnabledWhenAll = new[] { "Operation==Read", "PollingMode==WaitForValue" }, HiddenWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, IgnoredWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, ReasonCode = "SIEMENS_POLLING_VALUE_ONLY_WHEN_WAITING")]
[OperatorParameterRule("PollingTimeout", EnabledWhenAll = new[] { "Operation==Read", "PollingMode==WaitForValue" }, HiddenWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, IgnoredWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, ReasonCode = "SIEMENS_POLLING_TIMEOUT_ONLY_WHEN_WAITING")]
[OperatorParameterRule("PollingInterval", EnabledWhenAll = new[] { "Operation==Read", "PollingMode==WaitForValue" }, HiddenWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, IgnoredWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, ReasonCode = "SIEMENS_POLLING_INTERVAL_ONLY_WHEN_WAITING")]
[InputPort("Data", "数据", PortDataType.Any, IsRequired = false)]
[OutputPort("Response", "响应", PortDataType.String)]
[OutputPort("Status", "状态", PortDataType.Boolean)]
[OperatorParam("IpAddress", "IP地址", "string", DefaultValue = "192.168.0.1")]
[OperatorParam("Port", "端口", "int", DefaultValue = 102, Min = 1, Max = 65535)]
[OperatorParam("UseGlobalFallback", "允许全局回退", "bool", DefaultValue = false, Description = "启用后缺失的IP/Port可回退到全局通信配置")]
[OperatorParam("CpuType", "CPU类型", "enum", DefaultValue = "S71200", Options = new[] { "S7200|S7-200", "S7200Smart|S7-200 Smart", "S7300|S7-300", "S7400|S7-400", "S71200|S7-1200", "S71500|S7-1500" })]
[OperatorParam("Rack", "机架号", "int", DefaultValue = 0, Min = 0, Max = 15)]
[OperatorParam("Slot", "插槽号", "int", DefaultValue = 1, Min = 0, Max = 15)]
[OperatorParam("Address", "PLC地址", "string", DefaultValue = "DB1.DBW100")]
[OperatorParam("DataType", "数据类型", "enum", DefaultValue = "Word", Options = new[] { "Bit|位 (Bool)", "Byte|字节 (Byte)", "Word|字 (Word/UInt16)", "Int16|短整型 (Int16)", "DWord|双字 (DWord/UInt32)", "Int32|整型 (Int32)", "Float|浮点 (Float)", "Double|双精度 (Double)", "String|字符串 (String)" })]
[OperatorParam("Operation", "操作", "enum", DefaultValue = "Read", Options = new[] { "Read|读取", "Write|写入" })]
[OperatorParam("WriteValue", "写入值", "string", DefaultValue = "")]
[OperatorParam("PollingMode", "轮询模式", "enum", Description = "读取时是否启用轮询等待", DefaultValue = "None", Options = new[] { "None|不等待", "WaitForValue|等待指定值" })]
[OperatorParam("PollingCondition", "等待条件", "enum", Description = "等待的条件类型", DefaultValue = "Equal", Options = new[] { "Equal|等于", "NotEqual|不等于", "GreaterThan|大于", "LessThan|小于", "GreaterOrEqual|大于等于", "LessOrEqual|小于等于" })]
[OperatorParam("PollingValue", "等待值", "string", Description = "等待的目标值（如触发信号值）", DefaultValue = "1")]
[OperatorParam("PollingTimeout", "等待超时(ms)", "int", Description = "最长等待时间（毫秒）", DefaultValue = 30000, Min = 100, Max = 300000)]
[OperatorParam("PollingInterval", "轮询间隔(ms)", "int", Description = "每次读取间隔（毫秒）", DefaultValue = 50, Min = 10, Max = 5000)]
public class SiemensS7CommunicationOperator : PlcCommunicationOperatorBase
{
    private readonly Func<string, int, SiemensCpuType, int, int, IPlcClient> _clientFactory;

    public override OperatorType OperatorType => OperatorType.SiemensS7Communication;

    public SiemensS7CommunicationOperator(ILogger<SiemensS7CommunicationOperator> logger)
        : this(logger, CreateClient)
    {
    }

    internal SiemensS7CommunicationOperator(
        ILogger<SiemensS7CommunicationOperator> logger,
        Func<string, int, SiemensCpuType, int, int, IPlcClient> clientFactory)
        : base(logger)
    {
        _clientFactory = clientFactory;
    }

    protected override async Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        // 获取参数
        var operatorIpAddress = GetStringParam(@operator, "IpAddress", "");
        var operatorPort = GetIntParam(@operator, "Port", 0);
        var useGlobalFallback = GetBoolParam(@operator, "UseGlobalFallback", false);
        var cpuTypeStr = GetStringParam(@operator, "CpuType", "S71200");
        var rack = GetIntParam(@operator, "Rack", 0, 0, 15);
        var slot = GetIntParam(@operator, "Slot", 1, 0, 15);
        var address = GetStringParam(@operator, "Address", "DB1.DBW100");
        var dataType = GetStringParam(@operator, "DataType", "Word");
        var operation = GetStringParam(@operator, "Operation", "Read");

        // 【第二优先级】轮询等待模式参数
        var pollingMode = GetStringParam(@operator, "PollingMode", "None"); // None / WaitForValue
        var pollingCondition = GetStringParam(@operator, "PollingCondition", "Equal"); // Equal / NotEqual / GreaterThan / LessThan
        var pollingValue = GetStringParam(@operator, "PollingValue", "1");
        var pollingTimeout = GetIntParam(@operator, "PollingTimeout", 30000, 100, 300000); // 100ms - 5min
        var pollingInterval = GetIntParam(@operator, "PollingInterval", 50, 10, 5000); // 10ms - 5s

        if (!PlcOperatorParameterContract.IsSupportedOperation(operation))
        {
            return CreateFailureOutput("PLC_OPERATION_INVALID: Operation must be Read or Write.");
        }

        if (PlcOperatorParameterContract.IsRead(operation) &&
            !PlcOperatorParameterContract.IsSupportedPollingMode(pollingMode))
        {
            return CreateFailureOutput("PLC_POLLING_MODE_INVALID: PollingMode must be None or WaitForValue.");
        }

        if (PlcOperatorParameterContract.IsRead(operation) &&
            PlcOperatorParameterContract.IsWaitForValue(pollingMode) &&
            !PlcOperatorParameterContract.IsSupportedPollingCondition(pollingCondition))
        {
            return CreateFailureOutput(
                $"PLC_POLLING_CONDITION_INVALID: PollingCondition must be one of: {string.Join(", ", PlcOperatorParameterContract.SupportedPollingConditions)}.");
        }

        // 解析CPU类型
        var cpuType = cpuTypeStr.ToUpper() switch
        {
            "S7200" => SiemensCpuType.S7200,
            "S7200SMART" => SiemensCpuType.S7200Smart,
            "S7300" => SiemensCpuType.S7300,
            "S7400" => SiemensCpuType.S7400,
            "S71200" => SiemensCpuType.S71200,
            "S71500" => SiemensCpuType.S71500,
            _ => SiemensCpuType.S71200
        };

        var logIp = string.IsNullOrWhiteSpace(operatorIpAddress) ? "(unset)" : operatorIpAddress;
        var logPort = operatorPort;

        try
        {
            var (ipAddress, port, _, connectionSource) = ResolveConnectionSettings(
                operatorIpAddress,
                operatorPort,
                "S7",
                useGlobalFallback);
            logIp = ipAddress;
            logPort = port;

            // 构建连接键
            var connectionKey = $"S7:{ipAddress}:{port}:{cpuType}:{rack}:{slot}";

            // 获取或创建连接
            var (client, _) = await GetOrCreateConnectionAsync(
                connectionKey,
                () => _clientFactory(ipAddress, port, cpuType, rack, slot));

            if (PlcOperatorParameterContract.IsRead(operation))
            {
                // 【第二优先级】支持轮询等待模式
                if (PlcOperatorParameterContract.IsWaitForValue(pollingMode))
                {
                    var pollingReadOutput = await ExecuteWithConnectionOperationLockAsync(
                        connectionKey,
                        () => ExecuteReadWithPollingAsync(
                            client,
                            address,
                            dataType,
                            pollingCondition,
                            pollingValue,
                            pollingTimeout,
                            pollingInterval,
                            cancellationToken),
                        cancellationToken);
                    AttachConnectionAuditInfo(pollingReadOutput, connectionSource);
                    return pollingReadOutput;
                }

                var readOutput = await ExecuteWithConnectionOperationLockAsync(
                    connectionKey,
                    () => ExecuteReadAsync(client, address, dataType, cancellationToken),
                    cancellationToken);
                AttachConnectionAuditInfo(readOutput, connectionSource);
                return readOutput;
            }

            if (PlcOperatorParameterContract.IsWrite(operation))
            {
                var writeValue = ResolveWriteValue(@operator, inputs);
                var writeOutput = await ExecuteWithConnectionOperationLockAsync(
                    connectionKey,
                    () => ExecuteWriteAsync(client, address, dataType, writeValue, cancellationToken),
                    cancellationToken);
                AttachConnectionAuditInfo(writeOutput, connectionSource);
                return writeOutput;
            }

            return CreateFailureOutput("PLC_OPERATION_INVALID: Operation must be Read or Write.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[SiemensS7] 通信错误: {IP}:{Port} - {Message}", logIp, logPort, ex.Message);
            return CreateFailureOutput($"S7通信错误: {ex.Message}");
        }
    }

    private async Task<OperatorExecutionOutput> ExecuteReadAsync(
        IPlcClient client, string address, string dataType, CancellationToken ct)
    {
        var length = GetReadElementCount(dataType);
        var result = await client.ReadAsync(address, length, ct);

        if (!result.IsSuccess)
            return CreateFailureOutput($"读取失败: {result.Message}");

        var value = ConvertBytesToValue(client, result.Content!, dataType);
        Logger.LogInformation("[SiemensS7] 读取成功: {Address} = {Value}", address, value);
        return CreateSuccessOutput(value, dataType);
    }

    private async Task<OperatorExecutionOutput> ExecuteWriteAsync(
        IPlcClient client, string address, string dataType, string writeValue, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(writeValue))
            return CreateFailureOutput("写入值不能为空");

        var bytes = ConvertValueToBytes(client, writeValue, dataType);
        var result = await client.WriteAsync(address, bytes, ct);

        if (!result.IsSuccess)
            return CreateFailureOutput($"写入失败: {result.Message}");

        Logger.LogInformation("[SiemensS7] 写入成功: {Address} = {Value}", address, writeValue);
        return CreateSuccessOutput(writeValue, dataType);
    }

    /// <summary>
    /// 执行带轮询等待的读取操作
    /// </summary>
    private async Task<OperatorExecutionOutput> ExecuteReadWithPollingAsync(
        IPlcClient client, string address, string dataType,
        string pollingCondition, string pollingValue, int timeoutMs, int intervalMs, CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        int readCount = 0;

        Logger.LogInformation("[SiemensS7] 开始轮询等待: Address={Address}, Condition={Condition}, TargetValue={Target}, Timeout={Timeout}ms",
            address, pollingCondition, pollingValue, timeoutMs);

        while (true)
        {
            // 检查是否超时
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            if (elapsed > timeoutMs)
            {
                Logger.LogWarning("[SiemensS7] 轮询等待超时: Address={Address}, 已等待{Elapsed}ms", address, (int)elapsed);
                return CreateFailureOutput($"轮询等待超时: 等待{pollingCondition} {pollingValue}超过{timeoutMs}ms");
            }

            // 检查取消令牌
            ct.ThrowIfCancellationRequested();

            // 读取当前值
            var length = GetReadElementCount(dataType);
            var result = await client.ReadAsync(address, length, ct);

            if (!result.IsSuccess)
            {
                Logger.LogWarning("[SiemensS7] 轮询读取失败: {Message}", result.Message);
                await Task.Delay(Math.Min(intervalMs, 1000), ct); // 读取失败时延长等待
                continue;
            }

            var currentValue = ConvertBytesToValue(client, result.Content!, dataType);
            readCount++;

            // 检查是否满足条件
            if (PlcOperatorParameterContract.EvaluatePollingCondition(currentValue, pollingCondition, pollingValue))
            {
                var totalElapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                Logger.LogInformation("[SiemensS7] 轮询等待完成: Address={Address}, Value={Value}, 读取{Count}次, 耗时{Elapsed}ms",
                    address, currentValue, readCount, (int)totalElapsed);

                // 返回成功的输出，附加轮询信息
                var output = CreateSuccessOutput(currentValue, dataType);
                output.OutputData ??= new Dictionary<string, object>();
                output.OutputData["PollingReadCount"] = readCount;
                output.OutputData["PollingElapsedMs"] = (int)totalElapsed;
                output.OutputData["PollingMatched"] = true;
                return output;
            }

            // 等待下次轮询
            await Task.Delay(intervalMs, ct);
        }
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var operatorIpAddress = GetStringParam(@operator, "IpAddress", "");
        var operatorPort = GetIntParam(@operator, "Port", 0);
        var useGlobalFallback = GetBoolParam(@operator, "UseGlobalFallback", false);
        var address = GetStringParam(@operator, "Address", "");
        var operation = GetStringParam(@operator, "Operation", "Read");
        var pollingMode = GetStringParam(@operator, "PollingMode", "None");
        var pollingCondition = GetStringParam(@operator, "PollingCondition", "Equal");

        if (!PlcOperatorParameterContract.IsSupportedOperation(operation))
        {
            return ValidationResult.Invalid("PLC_OPERATION_INVALID: Operation must be Read or Write.");
        }

        if (PlcOperatorParameterContract.IsRead(operation) &&
            !PlcOperatorParameterContract.IsSupportedPollingMode(pollingMode))
        {
            return ValidationResult.Invalid("PLC_POLLING_MODE_INVALID: PollingMode must be None or WaitForValue.");
        }

        if (PlcOperatorParameterContract.IsRead(operation) &&
            PlcOperatorParameterContract.IsWaitForValue(pollingMode) &&
            !PlcOperatorParameterContract.IsSupportedPollingCondition(pollingCondition))
        {
            return ValidationResult.Invalid(
                $"PLC_POLLING_CONDITION_INVALID: PollingCondition must be one of: {string.Join(", ", PlcOperatorParameterContract.SupportedPollingConditions)}.");
        }

        try
        {
            ResolveConnectionSettings(operatorIpAddress, operatorPort, "S7", useGlobalFallback);
        }
        catch (Exception ex)
        {
            return ValidationResult.Invalid(ex.Message);
        }

        if (string.IsNullOrWhiteSpace(address))
            return ValidationResult.Invalid("PLC地址不能为空");

        return ValidationResult.Valid();
    }

    private static IPlcClient CreateClient(
        string ipAddress,
        int port,
        SiemensCpuType cpuType,
        int rack,
        int slot)
    {
        var client = PlcClientFactory.CreateSiemensS7(ipAddress, cpuType, rack, slot);
        if (client is SiemensS7Client typedClient)
        {
            typedClient.Port = port;
        }

        return client;
    }

    /// <summary>
    /// 解析写入值：优先从上游输入获取，否则使用参数面板静态值
    /// </summary>
    private string ResolveWriteValue(Operator @operator, Dictionary<string, object>? inputs)
    {
        // 获取参数面板中的静态值（作为fallback）
        var staticValue = GetStringParam(@operator, "WriteValue", "");

        if (inputs == null || inputs.Count == 0)
            return staticValue;

        // 按优先级顺序尝试从inputs获取动态值
        // 优先级：JudgmentValue > Value > Data > 静态值
        var priorityKeys = new[] { "JudgmentValue", "Value", "Data" };

        foreach (var key in priorityKeys)
        {
            if (inputs.TryGetValue(key, out var value) && value != null)
            {
                var stringValue = value.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(stringValue))
                {
                    Logger.LogDebug("[SiemensS7] 从上游获取动态值: Key={Key}, Value={Value}", key, stringValue);
                    return stringValue;
                }
            }
        }

        return staticValue;
    }
}
