using ClearVision.PlcComm;
using ClearVision.PlcComm.Interfaces;
using ClearVision.PlcComm.Mitsubishi;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "三菱MC通信",
    Description = "三菱 MC 协议 PLC 读写通信。",
    CategoryId = OperatorCategoryId.Communication,
    IconName = "mc-plc",
    Version = "1.1.0",
    Keywords = new[] { "PLC", "Mitsubishi", "MC", "Read", "Write" }
)]
[OperatorParameterRule("ProfileId", RequiredPolicy = OperatorParameterRequiredPolicy.Required, ResourceKind = OperatorResourceKind.PlcProfile, ReasonCode = "MITSUBISHI_PLC_PROFILE_REQUIRED")]
[OperatorParameterRule("Address", RequiredPolicy = OperatorParameterRequiredPolicy.Required, ResourceKind = OperatorResourceKind.PlcAddress, ReasonCode = "MITSUBISHI_PLC_ADDRESS_REQUIRED")]
[OperatorParameterRule("Operation", RequiredPolicy = OperatorParameterRequiredPolicy.Required, ReasonCode = "MITSUBISHI_PLC_OPERATION_REQUIRED")]
[OperatorParameterRule("Length", EnabledWhenAll = new[] { "Operation==Read" }, HiddenWhenAll = new[] { "Operation!=Read" }, IgnoredWhenAll = new[] { "Operation!=Read" }, ReasonCode = "MITSUBISHI_READ_LENGTH_ONLY_FOR_READ")]
[OperatorParameterRule("WriteValue", RequiredPolicy = OperatorParameterRequiredPolicy.Optional, EnabledWhenAll = new[] { "Operation==Write" }, HiddenWhenAll = new[] { "Operation!=Write" }, IgnoredWhenAll = new[] { "Operation!=Write" }, ReasonCode = "MITSUBISHI_WRITE_VALUE_ONLY_FOR_WRITE")]
[OperatorParameterRule("PollingMode", EnabledWhenAll = new[] { "Operation==Read" }, HiddenWhenAll = new[] { "Operation!=Read" }, IgnoredWhenAll = new[] { "Operation!=Read" }, ReasonCode = "MITSUBISHI_POLLING_ONLY_FOR_READ")]
[OperatorParameterRule("PollingCondition", EnabledWhenAll = new[] { "Operation==Read", "PollingMode==WaitForValue" }, HiddenWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, IgnoredWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, ReasonCode = "MITSUBISHI_POLLING_CONDITION_ONLY_WHEN_WAITING")]
[OperatorParameterRule("PollingValue", EnabledWhenAll = new[] { "Operation==Read", "PollingMode==WaitForValue" }, HiddenWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, IgnoredWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, ReasonCode = "MITSUBISHI_POLLING_VALUE_ONLY_WHEN_WAITING")]
[OperatorParameterRule("PollingTimeout", EnabledWhenAll = new[] { "Operation==Read", "PollingMode==WaitForValue" }, HiddenWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, IgnoredWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, ReasonCode = "MITSUBISHI_POLLING_TIMEOUT_ONLY_WHEN_WAITING")]
[OperatorParameterRule("PollingInterval", EnabledWhenAll = new[] { "Operation==Read", "PollingMode==WaitForValue" }, HiddenWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, IgnoredWhenAny = new[] { "Operation!=Read", "PollingMode!=WaitForValue" }, ReasonCode = "MITSUBISHI_POLLING_INTERVAL_ONLY_WHEN_WAITING")]
[InputPort("Data", "Data", PortDataType.Any, IsRequired = false)]
[OutputPort("Response", "Response", PortDataType.String)]
[OutputPort("Status", "Status", PortDataType.Boolean)]
[OperatorParam("ProfileId", "PLC Profile", "string", DefaultValue = "")]
[OperatorParam("Address", "PLC Address", "string", DefaultValue = "D100")]
[OperatorParam("Length", "Read Length", "int", DefaultValue = 1, Min = 1, Max = 999)]
[OperatorParam("Operation", "Operation", "enum", DefaultValue = "Read", Options = new[] { "Read|Read", "Write|Write" })]
[OperatorParam("WriteValue", "Write Value", "string", DefaultValue = "")]
[OperatorParam("PollingMode", "Polling Mode", "enum", Description = "Whether to poll while reading.", DefaultValue = "None", Options = new[] { "None|None", "WaitForValue|Wait For Value" })]
[OperatorParam("PollingCondition", "Polling Condition", "enum", Description = "Condition for polling.", DefaultValue = "Equal", Options = new[] { "Equal|Equal", "NotEqual|Not Equal", "GreaterThan|Greater Than", "LessThan|Less Than", "GreaterOrEqual|Greater Or Equal", "LessOrEqual|Less Or Equal" })]
[OperatorParam("PollingValue", "Polling Value", "string", Description = "Target value for polling.", DefaultValue = "1")]
[OperatorParam("PollingTimeout", "Polling Timeout (ms)", "int", Description = "Maximum wait duration in milliseconds.", DefaultValue = 30000, Min = 100, Max = 300000)]
[OperatorParam("PollingInterval", "Polling Interval (ms)", "int", Description = "Interval between polling reads in milliseconds.", DefaultValue = 50, Min = 10, Max = 5000)]
public sealed class MitsubishiMcCommunicationOperator : PlcCommunicationOperatorBase
{
    private readonly Func<string, int, IPlcClient> _clientFactory;

    public override OperatorType OperatorType => OperatorType.MitsubishiMcCommunication;

    public MitsubishiMcCommunicationOperator(ILogger<MitsubishiMcCommunicationOperator> logger)
        : this(logger, DenyAllExecutionResourceProfileResolver.Instance, CreateClient)
    {
    }

    public MitsubishiMcCommunicationOperator(
        ILogger<MitsubishiMcCommunicationOperator> logger,
        IExecutionResourceProfileResolver executionResourceProfileResolver)
        : this(logger, executionResourceProfileResolver, CreateClient)
    {
    }

    internal MitsubishiMcCommunicationOperator(
        ILogger<MitsubishiMcCommunicationOperator> logger,
        Func<string, int, IPlcClient> clientFactory)
        : this(logger, DenyAllExecutionResourceProfileResolver.Instance, clientFactory)
    {
    }

    internal MitsubishiMcCommunicationOperator(
        ILogger<MitsubishiMcCommunicationOperator> logger,
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
        var requestedAddress = GetStringParam(@operator, "Address", "D100");
        var length = GetIntParam(@operator, "Length", 1, 1, 999);
        var operation = GetStringParam(@operator, "Operation", "Read");
        var pollingMode = GetStringParam(@operator, "PollingMode", "None");
        var pollingCondition = GetStringParam(@operator, "PollingCondition", "Equal");
        var pollingValue = GetStringParam(@operator, "PollingValue", "1");
        var pollingTimeout = GetIntParam(@operator, "PollingTimeout", 30000, 100, 300000);
        var pollingInterval = GetIntParam(@operator, "PollingInterval", 50, 10, 5000);
        var writeValue = ResolveWriteValue(@operator, inputs);

        if (OperatorParameterValueSemantics.IsMissing(requestedAddress))
        {
            return CreateFailureOutput("Address cannot be empty.");
        }

        if (!operation.Equals("Read", StringComparison.OrdinalIgnoreCase) &&
            !operation.Equals("Write", StringComparison.OrdinalIgnoreCase))
        {
            return CreateFailureOutput("Operation must be Read or Write.");
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

        if (operation.Equals("Write", StringComparison.OrdinalIgnoreCase) &&
            OperatorParameterValueSemantics.IsMissing(writeValue))
        {
            return CreateFailureOutput("WriteValue cannot be empty.");
        }

        var logIp = "(unresolved)";
        var logPort = 0;

        try
        {
            var resolution = ResolveExecutionResource(
                profileId,
                ExecutionPlcProtocols.MitsubishiMc,
                requestedAddress,
                operation,
                operation.Equals("Read", StringComparison.OrdinalIgnoreCase) ? length : 1);
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

            var connectionKey = $"MC:{ipAddress}:{port}";
            await using var connectionLease = await AcquireConnectionLeaseAsync(
                connectionKey,
                () => _clientFactory(ipAddress, port),
                cancellationToken);
            var client = connectionLease.Client;

            if (operation.Equals("Read", StringComparison.OrdinalIgnoreCase))
            {
                if (pollingMode.Equals("WaitForValue", StringComparison.OrdinalIgnoreCase))
                {
                    var pollingReadOutput = await ExecuteWithConnectionOperationLockAsync(
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
                    AttachConnectionAuditInfo(pollingReadOutput, "ServerProfile");
                    return pollingReadOutput;
                }

                var readOutput = await ExecuteWithConnectionOperationLockAsync(
                    connectionKey,
                    () => ExecuteReadAsync(client, address, dataType, (ushort)length, cancellationToken),
                    cancellationToken);
                AttachConnectionAuditInfo(readOutput, "ServerProfile");
                return readOutput;
            }

            var writeOutput = await ExecuteWithConnectionOperationLockAsync(
                connectionKey,
                () => ExecuteWriteAsync(client, address, dataType, writeValue, cancellationToken),
                cancellationToken);
            AttachConnectionAuditInfo(writeOutput, "ServerProfile");
            return writeOutput;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[MitsubishiMC] Communication error: {IP}:{Port} - {Message}", logIp, logPort, ex.Message);
            return CreateFailureOutput($"MC communication error: {ex.Message}");
        }
    }

    private async Task<OperatorExecutionOutput> ExecuteReadAsync(
        IPlcClient client,
        string address,
        string dataType,
        ushort length,
        CancellationToken ct)
    {
        var result = await client.ReadAsync(address, length, ct);
        if (!result.IsSuccess)
        {
            return CreateFailureOutput($"Read failed: {result.Message}");
        }

        var value = ConvertBytesToValue(client, result.Content!, dataType);
        return CreateSuccessOutput(value, dataType);
    }

    private async Task<OperatorExecutionOutput> ExecuteWriteAsync(
        IPlcClient client,
        string address,
        string dataType,
        string writeValue,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(writeValue))
        {
            return CreateFailureOutput("WriteValue cannot be empty.");
        }

        var bytes = ConvertValueToBytes(client, writeValue, dataType);
        var result = await client.WriteAsync(address, bytes, ct);
        if (!result.IsSuccess)
        {
            return CreateFailureOutput($"Write failed: {result.Message}");
        }

        return CreateSuccessOutput(writeValue, dataType);
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
        var address = GetStringParam(@operator, "Address", string.Empty);
        var length = GetIntParam(@operator, "Length", 1);
        var pollingMode = GetStringParam(@operator, "PollingMode", "None");
        var pollingCondition = GetStringParam(@operator, "PollingCondition", "Equal");

        if (OperatorParameterValueSemantics.IsMissing(address))
        {
            return ValidationResult.Invalid("Address cannot be empty.");
        }

        var operation = GetStringParam(@operator, "Operation", "Read");
        if (!operation.Equals("Read", StringComparison.OrdinalIgnoreCase) &&
            !operation.Equals("Write", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("Operation must be Read or Write.");
        }

        if (operation.Equals("Read", StringComparison.OrdinalIgnoreCase) &&
            (length < 1 || length > 999))
        {
            return ValidationResult.Invalid("Length must be within [1, 999].");
        }

        if (operation.Equals("Read", StringComparison.OrdinalIgnoreCase))
        {
            if (!PlcOperatorParameterContract.IsSupportedPollingMode(pollingMode))
            {
                return ValidationResult.Invalid("PLC_POLLING_MODE_INVALID: PollingMode must be None or WaitForValue.");
            }

            if (PlcOperatorParameterContract.IsWaitForValue(pollingMode) &&
                !PlcOperatorParameterContract.IsSupportedPollingCondition(pollingCondition))
            {
                return ValidationResult.Invalid(
                    $"PLC_POLLING_CONDITION_INVALID: PollingCondition must be one of: {string.Join(", ", PlcOperatorParameterContract.SupportedPollingConditions)}.");
            }
        }

        var resolution = ResolveExecutionResource(
            profileId,
            ExecutionPlcProtocols.MitsubishiMc,
            address,
            operation,
            operation.Equals("Read", StringComparison.OrdinalIgnoreCase) ? length : 1);
        if (!resolution.Resolved || resolution.Resource == null)
        {
            return ValidationResult.Invalid($"{resolution.Code}: {resolution.Message}");
        }

        return ValidationResult.Valid();
    }

    private static IPlcClient CreateClient(string ipAddress, int port)
    {
        var client = PlcClientFactory.CreateMitsubishiMc(ipAddress);
        if (client is MitsubishiMcClient typedClient)
        {
            typedClient.Port = port;
        }

        return client;
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
        var startTime = DateTime.UtcNow;
        var readCount = 0;

        while (true)
        {
            var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
            if (elapsedMs > timeoutMs)
            {
                return CreateFailureOutput($"Polling timeout: waiting for {pollingCondition} {pollingValue} exceeded {timeoutMs}ms.");
            }

            ct.ThrowIfCancellationRequested();

            var result = await client.ReadAsync(address, length, ct);
            if (!result.IsSuccess)
            {
                await Task.Delay(Math.Min(intervalMs, 1000), ct);
                continue;
            }

            var currentValue = ConvertBytesToValue(client, result.Content!, dataType);
            readCount++;

            if (PlcOperatorParameterContract.EvaluatePollingCondition(currentValue, pollingCondition, pollingValue))
            {
                var output = CreateSuccessOutput(currentValue, dataType);
                output.OutputData ??= new Dictionary<string, object>();
                output.OutputData["PollingReadCount"] = readCount;
                output.OutputData["PollingElapsedMs"] = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
                output.OutputData["PollingMatched"] = true;
                return output;
            }

            await Task.Delay(intervalMs, ct);
        }
    }

    private static string ResolveWriteValue(Operator @operator, Dictionary<string, object>? inputs)
    {
        var staticValue = @operator.Parameters.FirstOrDefault(p => p.Name.Equals("WriteValue", StringComparison.OrdinalIgnoreCase))?.Value?.ToString() ?? string.Empty;
        if (inputs == null || inputs.Count == 0)
        {
            return staticValue;
        }

        foreach (var key in new[] { "JudgmentValue", "Value", "Data" })
        {
            if (!inputs.TryGetValue(key, out var value) || value == null)
            {
                continue;
            }

            var parsed = value.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(parsed))
            {
                return parsed;
            }
        }

        return staticValue;
    }
}
