using System.Reflection;
using System.Text.Json;
using Acme.Product.Application.Analysis;
using Acme.Product.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace Acme.Product.Tests.Services;

public class OutputPayloadSerializationTests
{
    [Fact]
    public void BuildSerializableOutputData_DegradesOnlyThrowingInstancesOfPreviouslySerializableType()
    {
        var outputData = new Dictionary<string, object>
        {
            ["Good"] = new ContentDependentJsonValue(throwOnSerialize: false),
            ["Bad"] = new ContentDependentJsonValue(throwOnSerialize: true)
        };

        var serializable = AnalysisPayloadSerialization.BuildSerializableOutputData(outputData);

        serializable["Good"].Should().BeOfType<JsonElement>();
        var good = (JsonElement)serializable["Good"]!;
        good.GetProperty(nameof(ContentDependentJsonValue.Value)).GetString().Should().Be("ok");
        serializable["Bad"].Should().Be("fallback-bad");

        var serialize = () => JsonSerializer.Serialize(serializable);
        serialize.Should().NotThrow();
    }

    [Fact]
    public void FlowExecutionOutputNormalization_DegradesOnlyThrowingInstancesOfPreviouslySerializableType()
    {
        var good = NormalizeWithPrivateMethod(
            typeof(FlowExecutionService),
            new ContentDependentJsonValue(throwOnSerialize: false));
        var bad = NormalizeWithPrivateMethod(
            typeof(FlowExecutionService),
            new ContentDependentJsonValue(throwOnSerialize: true));

        good.Should().BeOfType<JsonElement>();
        ((JsonElement)good!).GetProperty(nameof(ContentDependentJsonValue.Value)).GetString().Should().Be("ok");
        bad.Should().Be("fallback-bad");

        var serialize = () => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Good"] = good,
            ["Bad"] = bad
        });
        serialize.Should().NotThrow();
    }

    [Fact]
    public void OperatorPreviewOutputNormalization_DegradesOnlyThrowingInstancesOfPreviouslySerializableType()
    {
        var good = NormalizeWithPrivateMethod(
            typeof(OperatorPreviewService),
            new ContentDependentJsonValue(throwOnSerialize: false));
        var bad = NormalizeWithPrivateMethod(
            typeof(OperatorPreviewService),
            new ContentDependentJsonValue(throwOnSerialize: true));

        good.Should().BeOfType<JsonElement>();
        ((JsonElement)good!).GetProperty(nameof(ContentDependentJsonValue.Value)).GetString().Should().Be("ok");
        bad.Should().Be("fallback-bad");

        var serialize = () => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Good"] = good,
            ["Bad"] = bad
        });
        serialize.Should().NotThrow();
    }

    private static object? NormalizeWithPrivateMethod(Type ownerType, object value)
    {
        var method = ownerType.GetMethod(
            "TryNormalizeOutputValue",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        var args = new object?[] { value, null, 0 };
        var success = (bool)method!.Invoke(null, args)!;

        success.Should().BeTrue();
        return args[1];
    }

    private sealed class ContentDependentJsonValue
    {
        public ContentDependentJsonValue(bool throwOnSerialize)
        {
            ThrowOnSerialize = throwOnSerialize;
        }

        public bool ThrowOnSerialize { get; }

        public string Value => ThrowOnSerialize
            ? throw new InvalidOperationException("bad")
            : "ok";

        public override string ToString() => ThrowOnSerialize
            ? "fallback-bad"
            : "fallback-ok";
    }
}
