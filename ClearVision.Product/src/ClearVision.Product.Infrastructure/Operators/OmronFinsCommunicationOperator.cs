// OmronFinsCommunicationOperator.cs
// 解析写入值：优先从上游输入获取，否则使用参数面板静态值
// 作者：蘅芜君

using ClearVision.PlcComm;
using ClearVision.PlcComm.Interfaces;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Logging;
namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// 欧姆龙FINS通信算子
/// 支持FINS/TCP协议，适用于CP1H/CJ2M/NJ/NX系列PLC
/// </summary>
[OperatorMeta(
    DisplayName = "欧姆龙FINS通信",
    Description = "欧姆龙FINS/TCP协议PLC读写通信（CP1H/CJ2M/NJ/NX）",
    CategoryId = OperatorCategoryId.Communication,
    IconName = "fins",
    Version = "1.0.1"
)]
[OperatorParameterRule("ProfileId", RequiredPolicy = OperatorParameterRequiredPolicy.Required, ResourceKind = OperatorResourceKind.PlcProfile, ReasonCode = "OMRON_PLC_PROFILE_REQUIRED")]
[OperatorParameterRule("Address", RequiredPolicy = OperatorParameterRequiredPolicy.Required, ResourceKind = OperatorResourceKind.PlcAddress, ReasonCode = "OMRON_PLC_ADDRESS_REQUIRED")]
[OperatorParameterRule("Operation", RequiredPolicy = OperatorParameterRequiredPolicy.Required, ReasonCode = "OMRON_PLC_OPERATION_REQUIRED")]
[OperatorParameterRule("Length", EnabledWhenAll = new[] { "Operation==Read" }, HiddenWhenAll = new[] { "Operation!=Read" }, IgnoredWhenAll = new[] { "Operation!=Read" }, ReasonCode = "OMRON_READ_LENGTH_ONLY_FOR_READ")]
[OperatorParameterRule("WriteValue", RequiredPolicy = OperatorParameterRequiredPolicy.Optional, EnabledWhenAll = new[] { "Operation==Write" }, HiddenWhenAll = new[] { "Operation!=Write" }, IgnoredWhenAll = new[] { "Operation!=Write" }, ReasonCode = "OMRON_WRITE_VALUE_ONLY_FOR_WRITE")]
[OperatorParameterRule("PollingMode", EnabledWhenAll = new[] { "Operation==Read" }, HiddenWhenAll = new[] { "Operation!=Read" }, IgnoredWhenAll = new[] { "Operation!=Read" }, ReasonCode = "OMRON_POLLING_ONLY_FOR_READ")]
[OperatorParameterRule("PollingCondition", EnabledWhenAll = new[] { "Operation==Read", "PollingMode==WaitForValue" }, HiddenWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, IgnoredWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, ReasonCode = "OMRON_POLLING_CONDITION_ONLY_WHEN_WAITING")]
[OperatorParameterRule("PollingValue", EnabledWhenAll = new[] { "Operation==Read", "PollingMode==WaitForValue" }, HiddenWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, IgnoredWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, ReasonCode = "OMRON_POLLING_VALUE_ONLY_WHEN_WAITING")]
[OperatorParameterRule("PollingTimeout", EnabledWhenAll = new[] { "Operation==Read", "PollingMode==WaitForValue" }, HiddenWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, IgnoredWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, ReasonCode = "OMRON_POLLING_TIMEOUT_ONLY_WHEN_WAITING")]
[OperatorParameterRule("PollingInterval", EnabledWhenAll = new[] { "Operation==Read", "PollingMode==WaitForValue" }, HiddenWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, IgnoredWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, ReasonCode = "OMRON_POLLING_INTERVAL_ONLY_WHEN_WAITING")]
[InputPort("Data", "数据", PortDataType.Any, IsRequired = false)]
[OutputPort("Response", "响应", PortDataType.String)]
[OutputPort("Status", "状态", PortDataType.Boolean)]
[OperatorParam("ProfileId", "PLC Profile", "string", DefaultValue = "")]
[OperatorParam("Address", "PLC地址", "string", DefaultValue = "DM100")]
[OperatorParam("Length", "读取长度", "int", DefaultValue = 1, Min = 1, Max = 999)]
[OperatorParam("Operation", "操作", "enum", DefaultValue = "Read", Options = new[] { "Read|读取", "Write|写入" })]
[OperatorParam("WriteValue", "写入值", "string", DefaultValue = "")]
[OperatorParam("PollingMode", "轮询模式", "enum", Description = "读取时是否启用轮询等待", DefaultValue = "None", Options = new[] { "None|不等待", "WaitForValue|等待指定值" })]
[OperatorParam("PollingCondition", "等待条件", "enum", Description = "等待的条件类型", DefaultValue = "Equal", Options = new[] { "Equal|等于", "NotEqual|不等于", "GreaterThan|大于", "LessThan|小于", "GreaterOrEqual|大于等于", "LessOrEqual|小于等于" })]
[OperatorParam("PollingValue", "等待值", "string", Description = "等待的目标值（如触发信号值）", DefaultValue = "1")]
[OperatorParam("PollingTimeout", "等待超时(ms)", "int", Description = "最长等待时间（毫秒）", DefaultValue = 30000, Min = 100, Max = 300000)]
[OperatorParam("PollingInterval", "轮询间隔(ms)", "int", Description = "每次读取间隔（毫秒）", DefaultValue = 50, Min = 10, Max = 5000)]
public class OmronFinsCommunicationOperator : PlcCommunicationOperatorBase
{
    private readonly Func<string, int, IPlcClient> _clientFactory;

    public override OperatorType OperatorType => OperatorType.OmronFinsCommunication;

    public OmronFinsCommunicationOperator(ILogger<OmronFinsCommunicationOperator> logger)
        : this(logger, DenyAllExecutionResourceProfileResolver.Instance, CreateClient)
    {
    }

    public OmronFinsCommunicationOperator(
        ILogger<OmronFinsCommunicationOperator> logger,
        IExecutionResourceProfileResolver executionResourceProfileResolver)
        : this(logger, executionResourceProfileResolver, CreateClient)
    {
    }

    internal OmronFinsCommunicationOperator(
        ILogger<OmronFinsCommunicationOperator> logger,
        Func<string, int, IPlcClient> clientFactory)
        : this(logger, DenyAllExecutionResourceProfileResolver.Instance, clientFactory)
    {
    }

    internal OmronFinsCommunicationOperator(
        ILogger<OmronFinsCommunicationOperator> logger,
        IExecutionResourceProfileResolver executionResourceProfileResolver,
        Func<string, int, IPlcClient> clientFactory)
        : base(logger, executionResourceProfileResolver)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    protected override async Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var forbiddenRawTarget = FindForbiddenRawTargetParameter(
            @operator,
            "IpAddress",
            "Port",
            "UseGlobalFallback",
            "DataType");
        if (forbiddenRawTarget != null)
        {
            return CreateFailureOutput(
                $"PLC_RAW_TARGET_FORBIDDEN: {forbiddenRawTarget} cannot grant execution authority; use ProfileId and an allow-listed Address/Operation binding.");
        }

        var profileId = GetStringParam(@operator, "ProfileId", string.Empty);
        var requestedAddress = GetStringParam(@operator, "Address", "DM100");
        var length = GetIntParam(@operator, "Length", 1, 1, 999);
        var operation = GetStringParam(@operator, "Operation", "Read");
        var pollingMode = GetStringParam(@operator, "PollingMode", "None");
        var pollingCondition = GetStringParam(@operator, "PollingCondition", "Equal");
        var pollingValue = GetStringParam(@operator, "PollingValue", "1");
        var pollingTimeout = GetIntParam(@operator, "PollingTimeout", 30000, 100, 300000);
        var pollingInterval = GetIntParam(@operator, "PollingInterval", 50, 10, 5000);

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

        var logIp = "(unresolved)";
        var logPort = 0;

        try
        {
            var resolution = ResolveExecutionResource(
                profileId,
                ExecutionPlcProtocols.OmronFins,
                requestedAddress,
                operation,
                PlcOperatorParameterContract.IsRead(operation) ? length : 1);
            if (!resolution.Resolved || resolution.Resource == null)
            {
                return CreateFailureOutput($"{resolution.Code}: {resolution.Message}");
            }

            var resource = resolution.Resource;
            var ipAddress = resource.Host;
            var port = resource.Port;
            var address = resource.Address;
            var dataType = resource.DataType;
            logIp = ipAddress;
            logPort = port;

            // 构建连接键
            var connectionKey = $"FINS:{ipAddress}:{port}";

            // 获取带生命周期保护的池连接；最后一个 lease 释放后才物理关闭。
            await using var connectionLease = await AcquireConnectionLeaseAsync(
                connectionKey,
                () => _clientFactory(ipAddress, port),
                cancellationToken);
            var client = connectionLease.Client;

            if (PlcOperatorParameterContract.IsRead(operation))
            {
                if (PlcOperatorParameterContract.IsWaitForValue(pollingMode))
                {
                    var pollingOutput = await ExecuteWithConnectionOperationLockAsync(
                        connectionKey,
                        () => ExecuteReadWithPollingAsync(
                            client,
                            address,
                            dataType,
                            (ushort)length,
                            pollingCondition,
                            pollingValue,
                            pollingTimeout,
                            pollingInterval,
                            cancellationToken),
                        cancellationToken);
                    AttachConnectionAuditInfo(pollingOutput, "ServerProfile");
                    return pollingOutput;
                }

                var readOutput = await ExecuteWithConnectionOperationLockAsync(
                    connectionKey,
                    () => ExecuteReadAsync(client, address, dataType, (ushort)length, cancellationToken),
                    cancellationToken);
                AttachConnectionAuditInfo(readOutput, "ServerProfile");
                return readOutput;
            }

            if (PlcOperatorParameterContract.IsWrite(operation))
            {
                var writeValue = ResolveWriteValue(@operator, inputs);
                var writeOutput = await ExecuteWithConnectionOperationLockAsync(
                    connectionKey,
                    () => ExecuteWriteAsync(client, address, dataType, writeValue, cancellationToken),
                    cancellationToken);
                AttachConnectionAuditInfo(writeOutput, "ServerProfile");
                return writeOutput;
            }

            return CreateFailureOutput("PLC_OPERATION_INVALID: Operation must be Read or Write.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[OmronFINS] 通信错误: {IP}:{Port} - {Message}", logIp, logPort, ex.Message);
            return CreateFailureOutput($"FINS通信错误: {ex.Message}");
        }
    }

    private async Task<OperatorExecutionOutput> ExecuteReadAsync(
        IPlcClient client, string address, string dataType, ushort length, CancellationToken ct)
    {
        var result = await client.ReadAsync(address, length, ct);

        if (!result.IsSuccess)
            return CreateFailureOutput($"读取失败: {result.Message}");

        var value = ConvertBytesToValue(client, result.Content!, dataType);
        Logger.LogInformation("[OmronFINS] 读取成功: {Address} = {Value}", address, value);
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

        Logger.LogInformation("[OmronFINS] 写入成功: {Address} = {Value}", address, writeValue);
        return CreateSuccessOutput(writeValue, dataType);
    }

    private async Task<OperatorExecutionOutput> ExecuteReadWithPollingAsync(
        IPlcClient client,
        string address,
        string dataType,
        ushort length,
        string pollingCondition,
        string pollingValue,
        int timeoutMs,
        int intervalMs,
        CancellationToken ct)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var readCount = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (stopwatch.ElapsedMilliseconds >= timeoutMs)
            {
                return CreateFailureOutput(
                    $"FINS_POLLING_TIMEOUT: Waiting for {pollingCondition} {pollingValue} exceeded {timeoutMs}ms.");
            }

            var result = await client.ReadAsync(address, length, ct);
            if (result.IsSuccess)
            {
                var currentValue = ConvertBytesToValue(client, result.Content!, dataType);
                readCount++;
                if (PlcOperatorParameterContract.EvaluatePollingCondition(
                        currentValue,
                        pollingCondition,
                        pollingValue))
                {
                    var output = CreateSuccessOutput(currentValue, dataType);
                    output.OutputData ??= new Dictionary<string, object>();
                    output.OutputData["PollingReadCount"] = readCount;
                    output.OutputData["PollingElapsedMs"] = (int)stopwatch.ElapsedMilliseconds;
                    output.OutputData["PollingMatched"] = true;
                    return output;
                }
            }

            var remainingMs = timeoutMs - (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue);
            if (remainingMs <= 0)
            {
                return CreateFailureOutput(
                    $"FINS_POLLING_TIMEOUT: Waiting for {pollingCondition} {pollingValue} exceeded {timeoutMs}ms.");
            }

            await Task.Delay(Math.Min(intervalMs, remainingMs), ct);
        }
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var forbiddenRawTarget = FindForbiddenRawTargetParameter(
            @operator,
            "IpAddress",
            "Port",
            "UseGlobalFallback",
            "DataType");
        if (forbiddenRawTarget != null)
        {
            return ValidationResult.Invalid(
                $"PLC_RAW_TARGET_FORBIDDEN: {forbiddenRawTarget} cannot grant execution authority; use ProfileId and an allow-listed Address/Operation binding.");
        }

        var profileId = GetStringParam(@operator, "ProfileId", string.Empty);
        var address = GetStringParam(@operator, "Address", "");
        var length = GetIntParam(@operator, "Length", 1);
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

        if (string.IsNullOrWhiteSpace(address))
            return ValidationResult.Invalid("PLC地址不能为空");

        if (PlcOperatorParameterContract.IsRead(operation) && (length < 1 || length > 999))
            return ValidationResult.Invalid("读取长度必须在 1-999 之间");

        var resolution = ResolveExecutionResource(
            profileId,
            ExecutionPlcProtocols.OmronFins,
            address,
            operation,
            PlcOperatorParameterContract.IsRead(operation) ? length : 1);
        if (!resolution.Resolved || resolution.Resource == null)
        {
            return ValidationResult.Invalid($"{resolution.Code}: {resolution.Message}");
        }

        return ValidationResult.Valid();
    }

    private static IPlcClient CreateClient(string ipAddress, int port)
    {
        var client = PlcClientFactory.CreateOmronFins(ipAddress);
        if (client is ClearVision.PlcComm.Omron.OmronFinsClient typedClient)
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
                    Logger.LogDebug("[OmronFINS] 从上游获取动态值: Key={Key}, Value={Value}", key, stringValue);
                    return stringValue;
                }
            }
        }

        return staticValue;
    }
}
