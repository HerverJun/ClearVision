// MqttPublishOperator.cs
// MQTT publish operator placeholder.

using System.Text.Json;
using Acme.Product.Core.Attributes;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Infrastructure.Operators;

/// <summary>
/// MQTT publish placeholder. It validates parameters and fails fast until the
/// MQTT client dependency and connection lifecycle are explicitly enabled.
/// </summary>
[OperatorMeta(
    DisplayName = "MQTT Publish",
    Description = "Publishes inspection data to MQTT when the optional MQTT integration is enabled.",
    Category = "Communication",
    IconName = "mqtt",
    Keywords = new[] { "MQTT", "IoT", "Publish", "Communication" },
    Tags = new[] { "maturity:placeholder-disabled", "integration:mqtt", "experimental" },
    Version = "0.1.0"
)]
[InputPort("Payload", "Message Payload", PortDataType.Any, IsRequired = true)]
[InputPort("Message", "Message Text", PortDataType.String, IsRequired = false)]
[OutputPort("IsSuccess", "Is Success", PortDataType.Boolean)]
[OperatorParam("Broker", "Broker Address", "string", DefaultValue = "localhost")]
[OperatorParam("Port", "Port", "int", DefaultValue = 1883)]
[OperatorParam("Topic", "Topic", "string", DefaultValue = "cv/results")]
[OperatorParam("Qos", "QoS", "int", DefaultValue = 1)]
[OperatorParam("Retain", "Retain Message", "bool", DefaultValue = false)]
[OperatorParam("TimeoutMs", "Timeout (ms)", "int", DefaultValue = 5000)]
public class MqttPublishOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.MqttPublish;

    public MqttPublishOperator(ILogger<MqttPublishOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var broker = GetStringParam(@operator, "Broker", "localhost");
        var port = GetIntParam(@operator, "Port", 1883, 1, 65535);
        var topic = GetStringParam(@operator, "Topic", "");
        var qos = GetQosParam(@operator, 0);
        var retain = GetBoolParam(@operator, "Retain", false);
        var timeoutMs = GetIntParam(@operator, "TimeoutMs", 5000, 1000, 30000);

        if (string.IsNullOrWhiteSpace(topic))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Topic parameter cannot be empty."));
        }

        string message;
        if (TryGetInputValue(inputs, "Payload", out var payloadObj) && payloadObj != null)
        {
            message = payloadObj is string payloadText
                ? payloadText
                : JsonSerializer.Serialize(payloadObj);
        }
        else if (TryGetInputValue(inputs, "Message", out var msgObj) && msgObj != null)
        {
            message = msgObj.ToString() ?? "";
        }
        else if (inputs != null && inputs.Count > 0)
        {
            message = JsonSerializer.Serialize(inputs);
        }
        else
        {
            message = "{}";
        }

        Logger.LogWarning(
            "[MqttPublish] MQTT publish requested for {Broker}:{Port}/{Topic}. Qos={Qos}, Retain={Retain}, TimeoutMs={TimeoutMs}, PayloadLength={PayloadLength}. The runtime integration is placeholder-disabled in this build.",
            broker,
            port,
            topic,
            qos,
            retain,
            timeoutMs,
            message.Length);

        return Task.FromResult(OperatorExecutionOutput.Failure(
            "MQTT publish is placeholder-disabled in this build. Enable the MQTT client integration before using this operator."));
    }

    private static bool TryGetInputValue(
        Dictionary<string, object>? inputs,
        string key,
        out object? value)
    {
        value = null;
        if (inputs == null)
        {
            return false;
        }

        if (inputs.TryGetValue(key, out value))
        {
            return true;
        }

        var match = inputs.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(match.Key))
        {
            return false;
        }

        value = match.Value;
        return true;
    }

    private static int GetQosParam(Operator @operator, int defaultValue)
    {
        var param = @operator.Parameters.FirstOrDefault(p =>
            string.Equals(p.Name, "Qos", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Name, "QoS", StringComparison.OrdinalIgnoreCase));

        if (param?.GetValue() == null)
        {
            return defaultValue;
        }

        try
        {
            return Math.Clamp(Convert.ToInt32(param.GetValue()), 0, 2);
        }
        catch
        {
            return defaultValue;
        }
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var broker = GetStringParam(@operator, "Broker", "");
        var topic = GetStringParam(@operator, "Topic", "");
        var qos = GetQosParam(@operator, 0);

        if (string.IsNullOrWhiteSpace(broker))
        {
            return ValidationResult.Invalid("Broker cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(topic))
        {
            return ValidationResult.Invalid("Topic cannot be empty.");
        }

        if (qos < 0 || qos > 2)
        {
            return ValidationResult.Invalid("QoS must be 0, 1, or 2.");
        }

        return ValidationResult.Valid();
    }
}
