using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

public class ComparatorOperatorTests
{
    private readonly ComparatorOperator _operator = new(Substitute.For<ILogger<ComparatorOperator>>());

    [Fact]
    public async Task ExecuteAsync_WithCompareValueFallback_ShouldCompareAgainstConfiguredValue()
    {
        var op = new Operator("cmp", OperatorType.Comparator, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Condition", "GreaterThan", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", 10.0, "double"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["ValueA"] = 12.0
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["Difference"].Should().Be(2.0);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutValueA_ShouldFailClosed()
    {
        var op = new Operator("cmp", OperatorType.Comparator, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Condition", "GreaterThan", "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ValueA");
    }

    [Fact]
    public async Task ExecuteAsync_WithNullValueB_ShouldFallbackToCompareValue()
    {
        var op = new Operator("cmp", OperatorType.Comparator, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Condition", "GreaterThan", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", 10.0, "double"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["ValueA"] = 12.0,
            ["ValueB"] = null!
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["Difference"].Should().Be(2.0);
    }
}

public class DelayOperatorTests
{
    private readonly DelayOperator _operator = new(Substitute.For<ILogger<DelayOperator>>());

    [Fact]
    public async Task ExecuteAsync_ShouldDelayAndPassThroughInput()
    {
        var op = new Operator("delay", OperatorType.Delay, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Milliseconds", 25, "int"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Input"] = "payload"
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["Output"].Should().Be("payload");
        Convert.ToInt32(result.OutputData["ElapsedMs"]).Should().BeGreaterThanOrEqualTo(15);
    }

    [Fact]
    public void ValidateParameters_WithNegativeMilliseconds_ShouldBeInvalid()
    {
        var op = new Operator("delay", OperatorType.Delay, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Milliseconds", -1, "int"));

        var validation = _operator.ValidateParameters(op);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().Contain("greater than or equal to 0");
    }

    [Fact]
    public async Task ExecuteAsync_WithTooLargeDelay_ShouldFailPredictably()
    {
        var op = new Operator("delay", OperatorType.Delay, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Milliseconds", 60001, "int"));

        var result = await _operator.ExecuteAsync(op);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("60000");
    }

    [Fact]
    public async Task ExecuteAsync_WithCanceledToken_ShouldThrowOperationCanceledException()
    {
        var op = new Operator("delay", OperatorType.Delay, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Milliseconds", 25, "int"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => _operator.ExecuteAsync(op, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

public class VariableReadOperatorTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReadTypedValueFromContext()
    {
        var context = new VariableContext();
        context.SetValue("temperature", 42.5);
        var sut = new VariableReadOperator(Substitute.For<ILogger<VariableReadOperator>>(), context);
        var op = new Operator("read", OperatorType.VariableRead, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "temperature", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["Exists"].Should().Be(true);
        result.OutputData["Value"].Should().Be(42.5);
    }

    [Fact]
    public async Task ExecuteAsync_WhenProjectScopeUsesExpressionAndFloorConversion_ShouldReadProjectedInt64Value()
    {
        var variableId = Guid.NewGuid();
        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "stats.score",
                    DisplayName = "Score",
                    ValueType = ProjectGlobalVariableValueType.Double,
                    InitialValue = JsonSerializer.SerializeToElement(2.5)
                }
            ]
        };
        using var session = new ProjectVariableSession(schema);
        var accessor = new ProjectVariableExecutionContextAccessor();
        using var scope = accessor.BeginScope(new ProjectVariableExecutionContext(session, ProjectVariableBindingIndex.Build(schema), Guid.NewGuid()));
        var sut = new VariableReadOperator(Substitute.For<ILogger<VariableReadOperator>>(), new VariableContext(), accessor);
        var op = new Operator("read", OperatorType.VariableRead, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Scope", "Project", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("VariableId", variableId.ToString(), "string"));
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "stats.score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DefaultValue", "0", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Int", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ConversionMode", "Floor", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Expression", "value * 2 + 0.5", "string"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Value"].Should().Be(5L);
        result.OutputData["Exists"].Should().Be(true);
    }
}

public class VariableWriteOperatorTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldWriteInputValueIntoContext()
    {
        var context = new VariableContext();
        var sut = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), context);
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "batchId", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "String", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = "LAB-001"
        });

        result.IsSuccess.Should().BeTrue();
        context.GetValue<string>("batchId").Should().Be("LAB-001");
    }

    [Fact]
    public async Task ExecuteAsync_WhenProjectScopeUsesExpressionAndRoundConversion_ShouldWriteRoundedInt64Value()
    {
        var variableId = Guid.NewGuid();
        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "stats.count",
                    DisplayName = "Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(0L)
                }
            ]
        };
        using var session = new ProjectVariableSession(schema);
        var accessor = new ProjectVariableExecutionContextAccessor();
        using var scope = accessor.BeginScope(new ProjectVariableExecutionContext(session, ProjectVariableBindingIndex.Build(schema), Guid.NewGuid()));
        var sut = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), new VariableContext(), accessor);
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Scope", "Project", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("VariableId", variableId.ToString(), "string"));
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "stats.count", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));
        op.AddParameter(TestHelpers.CreateParameter("UseInputValue", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("StaticValue", "0", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ConversionMode", "Round", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Expression", "value * 1.5", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = 2.25
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        session.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(3L);
    }
}

public class VariableIncrementOperatorTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldIncrementAndExposeResetState()
    {
        var context = new VariableContext();
        context.SetValue("counter", 5L);
        var sut = new VariableIncrementOperator(Substitute.For<ILogger<VariableIncrementOperator>>(), context);
        var op = new Operator("inc", OperatorType.VariableIncrement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "counter", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Delta", 2, "int"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["PreviousValue"].Should().Be(5L);
        result.OutputData["NewValue"].Should().Be(7L);
        result.OutputData["WasReset"].Should().Be(false);
    }

    [Fact]
    public async Task ExecuteAsync_WhenResetConditionMatches_ShouldWriteResetValueBackToContext()
    {
        var context = new VariableContext();
        context.SetValue("counter", 10L);
        var sut = new VariableIncrementOperator(Substitute.For<ILogger<VariableIncrementOperator>>(), context);
        var op = new Operator("inc_reset", OperatorType.VariableIncrement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "counter", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Delta", 2, "int"));
        op.AddParameter(TestHelpers.CreateParameter("ResetCondition", "GreaterThan", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResetThreshold", 5, "int"));
        op.AddParameter(TestHelpers.CreateParameter("ResetValue", 1, "int"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["WasReset"].Should().Be(true);
        result.OutputData["NewValue"].Should().Be(3L);
        context.GetValue<long>("counter").Should().Be(3L);
    }
}

public class CycleCounterOperatorTests
{
    [Fact]
    public async Task ExecuteAsync_IncrementAction_ShouldAdvanceCycleCount()
    {
        var context = new VariableContext();
        var sut = new CycleCounterOperator(Substitute.For<ILogger<CycleCounterOperator>>(), context);
        var op = new Operator("cycle", OperatorType.CycleCounter, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Action", "Increment", "string"));
        op.AddParameter(TestHelpers.CreateParameter("MaxCycles", 3, "int"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["CycleCount"].Should().Be(1L);
        result.OutputData["IsLimitReached"].Should().Be(false);
    }

    [Fact]
    public void ValidateParameters_WithUnsupportedAction_ShouldBeInvalid()
    {
        var sut = new CycleCounterOperator(Substitute.For<ILogger<CycleCounterOperator>>(), new VariableContext());
        var op = new Operator("cycle", OperatorType.CycleCounter, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Action", "Skip", "string"));

        var validation = sut.ValidateParameters(op);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().Contain("Unsupported action");
    }

    [Fact]
    public void ValidateParameters_WithNegativeMaxCycles_ShouldBeInvalid()
    {
        var sut = new CycleCounterOperator(Substitute.For<ILogger<CycleCounterOperator>>(), new VariableContext());
        var op = new Operator("cycle", OperatorType.CycleCounter, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Action", "Read", "string"));
        op.AddParameter(TestHelpers.CreateParameter("MaxCycles", -1, "int"));

        var validation = sut.ValidateParameters(op);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().Contain("MaxCycles");
    }

    [Fact]
    public async Task ExecuteAsync_WhenLimitAlreadyReached_ShouldNotIncrementBeyondMaxCycles()
    {
        var context = new VariableContext();
        context.IncrementCycleCount();
        context.IncrementCycleCount();

        var sut = new CycleCounterOperator(Substitute.For<ILogger<CycleCounterOperator>>(), context);
        var op = new Operator("cycle", OperatorType.CycleCounter, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Action", "Increment", "string"));
        op.AddParameter(TestHelpers.CreateParameter("MaxCycles", 2, "int"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["CycleCount"].Should().Be(2L);
        result.OutputData["IsLimitReached"].Should().Be(true);
        result.OutputData["RemainingCycles"].Should().Be(0L);
        Convert.ToDouble(result.OutputData["Progress"]).Should().Be(100d);
        context.CycleCount.Should().Be(2L);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCycleCountWouldOverflow_ShouldFailWithoutIncrementing()
    {
        var context = Substitute.For<IVariableContext>();
        context.CycleCount.Returns(long.MaxValue);
        var sut = new CycleCounterOperator(Substitute.For<ILogger<CycleCounterOperator>>(), context);

        var op = new Operator("cycle", OperatorType.CycleCounter, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Action", "Increment", "string"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Int64.MaxValue");
        context.DidNotReceive().IncrementCycleCount();
    }
}

public class StringFormatOperatorTests
{
    private readonly StringFormatOperator _operator = new(Substitute.For<ILogger<StringFormatOperator>>());

    [Fact]
    public async Task ExecuteAsync_TemplateMode_ShouldReplaceIndexedAndNamedPlaceholders()
    {
        var op = new Operator("format", OperatorType.StringFormat, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Template", "Result={0}; Name={Name}", "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Arg1"] = "OK",
            ["Name"] = "StationA"
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["Result"].Should().Be("Result=OK; Name=StationA");
    }
}

public class CommentOperatorTests
{
    private readonly CommentOperator _operator = new(Substitute.For<ILogger<CommentOperator>>());

    [Fact]
    public async Task ExecuteAsync_ShouldPassThroughInputAndExposeMessage()
    {
        var op = new Operator("comment", OperatorType.Comment, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Text", "lab checkpoint", "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Input"] = "payload"
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["Output"].Should().Be("payload");
        result.OutputData["Message"].Should().Be("lab checkpoint");
    }

    [Fact]
    public async Task ExecuteAsync_WithReferencePayload_ShouldOnlyPassThroughAndExposeMessage()
    {
        var payload = new Dictionary<string, object> { ["Name"] = "station-a" };
        var op = new Operator("comment", OperatorType.Comment, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Text", "checkpoint", "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Input"] = payload
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["Output"].Should().BeSameAs(payload);
        result.OutputData["Message"].Should().Be("checkpoint");
    }

    [Fact]
    public async Task ExecuteAsync_WithImagePayload_ShouldPreserveImageReference()
    {
        var payload = TestHelpers.CreateTestImage(width: 24, height: 16);
        var op = new Operator("comment", OperatorType.Comment, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Text", "image checkpoint", "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Input"] = payload
        });

        result.IsSuccess.Should().BeTrue();
        var output = result.OutputData!["Output"].Should().BeOfType<ImageWrapper>().Subject;
        output.Should().BeSameAs(payload);
        output.RefCount.Should().Be(1);
        result.OutputData["Message"].Should().Be("image checkpoint");

        output.Release();
    }

    [Fact]
    public void ValidateParameters_ShouldRejectOversizedText()
    {
        var op = new Operator("comment", OperatorType.Comment, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Text", new string('x', 4097), "string"));

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("4096");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenTextExceedsContractLimit()
    {
        var op = new Operator("comment", OperatorType.Comment, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Text", new string('x', 4097), "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Input"] = "payload"
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("4096");
    }
}
