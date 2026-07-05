using System.Reflection;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

public class MqttPublishOperatorTests
{
    private readonly MqttPublishOperator _operator;

    public MqttPublishOperatorTests()
    {
        _operator = new MqttPublishOperator(Substitute.For<ILogger<MqttPublishOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeMqttPublish()
    {
        _operator.OperatorType.Should().Be(OperatorType.MqttPublish);
    }

    [Fact]
    public void Metadata_ShouldDeclarePlaceholderDisabledMaturity()
    {
        var metadata = typeof(MqttPublishOperator).GetCustomAttribute<OperatorMetaAttribute>();

        metadata.Should().NotBeNull();
        metadata!.Tags.Should().Contain("maturity:placeholder-disabled");
        metadata.Tags.Should().Contain("integration:mqtt");
    }

    [Fact]
    public async Task ExecuteAsync_WithPayloadInput_ShouldFailFastInsteadOfPretendingSuccess()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Broker", "localhost" },
            { "Topic", "cv/results" },
            { "Qos", 1 },
            { "TimeoutMs", 3000 }
        });

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            { "Payload", new Dictionary<string, object> { { "status", "NG" } } }
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("[MQTT_PUBLISH_DISABLED]");
        result.ErrorMessage.Should().Contain("当前版本未启用 MQTT 发布能力");
        result.ErrorMessage.Should().NotContain("placeholder-disabled");
        result.OutputData.Should().BeNull();
    }

    [Fact]
    public void ValidateParameters_WithLegacyQoSCasing_ShouldStillBeValid()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Broker", "localhost" },
            { "Topic", "cv/results" },
            { "QoS", 2 }
        });

        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    private static Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        var op = new Operator("MqttPublish", OperatorType.MqttPublish, 0, 0);

        if (parameters != null)
        {
            foreach (var (name, value) in parameters)
            {
                op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, "string", value));
            }
        }

        return op;
    }
}
