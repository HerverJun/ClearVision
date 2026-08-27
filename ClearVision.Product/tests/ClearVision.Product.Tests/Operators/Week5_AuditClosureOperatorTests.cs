using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
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

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
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

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class VariableReadOperatorTests
{
    public static IEnumerable<object[]> RunIntegerConversionCases()
    {
        yield return new object[] { 2.5d, "Round", 3L };
        yield return new object[] { 2.9m, "Floor", 2L };
        yield return new object[] { "2.1", "Ceiling", 3L };
        yield return new object[] { "-2.9", "Truncate", -2L };
        yield return new object[] { "42", "Exact", 42L };
    }

    public static IEnumerable<object[]> RunIntegerExactFractionCases()
    {
        yield return new object[] { 2.5d };
        yield return new object[] { 2.5m };
        yield return new object[] { "2.5" };
    }

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

    [Theory]
    [MemberData(nameof(RunIntegerConversionCases))]
    public async Task ExecuteAsync_WithRunScopeIntConversionMode_ShouldConvertFractionalNumericValues(
        object rawValue,
        string conversionMode,
        long expectedValue)
    {
        var context = new VariableContext();
        context.SetValue("score", rawValue);
        var sut = new VariableReadOperator(Substitute.For<ILogger<VariableReadOperator>>(), context);
        var op = new Operator("read", OperatorType.VariableRead, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Int", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ConversionMode", conversionMode, "string"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Value"].Should().Be(expectedValue);
        result.OutputData["ReadSource"].Should().Be("RunVariable");
    }

    [Theory]
    [MemberData(nameof(RunIntegerExactFractionCases))]
    public async Task ExecuteAsync_WithRunScopeIntExactAndFraction_ShouldFailWithoutDefaultFallback(object rawValue)
    {
        var context = new VariableContext();
        context.SetValue("score", rawValue);
        var sut = new VariableReadOperator(Substitute.For<ILogger<VariableReadOperator>>(), context);
        var op = new Operator("read", OperatorType.VariableRead, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Int", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ConversionMode", "Exact", "string"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Fractional numeric values require");
    }

    [Fact]
    public async Task ExecuteAsync_WithOutputFieldName_ShouldReadNestedRunVariableField()
    {
        var context = new VariableContext();
        var rawValue = new Dictionary<string, object>
        {
            ["ParsedFields"] = new Dictionary<string, object>
            {
                ["Score"] = 98.5,
                ["Status"] = "OK"
            }
        };
        context.SetValue("tcp.lastResult", rawValue);
        var sut = new VariableReadOperator(Substitute.For<ILogger<VariableReadOperator>>(), context);
        var op = new Operator("read", OperatorType.VariableRead, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "tcp.lastResult", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));
        op.AddParameter(TestHelpers.CreateParameter("OutputFieldName", "ParsedFields.Score", "string"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Value"].Should().Be(98.5);
        result.OutputData["RawValue"].Should().BeSameAs(rawValue);
        result.OutputData["ReadSource"].Should().Be("RunVariableField");
        result.OutputData["OutputFieldName"].Should().Be("ParsedFields.Score");
        result.OutputData["OutputFieldFound"].Should().Be(true);
    }

    [Fact]
    public async Task ExecuteAsync_WithJsonStringOutputFieldName_ShouldReadStatusAsBool()
    {
        var context = new VariableContext();
        context.SetValue("tcp.lastJson", """{"ParsedFields":{"Score":98.5,"Status":"OK"}}""");
        var sut = new VariableReadOperator(Substitute.For<ILogger<VariableReadOperator>>(), context);
        var op = new Operator("read", OperatorType.VariableRead, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "tcp.lastJson", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Bool", "string"));
        op.AddParameter(TestHelpers.CreateParameter("OutputFieldName", "ParsedFields.Status", "string"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Value"].Should().Be(true);
        result.OutputData["RawValue"].Should().Be("""{"ParsedFields":{"Score":98.5,"Status":"OK"}}""");
        result.OutputData["ReadSource"].Should().Be("RunVariableField");
        result.OutputData["OutputFieldFound"].Should().Be(true);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingOutputFieldName_ShouldFailByDefault()
    {
        var context = new VariableContext();
        context.SetValue("tcp.lastResult", new Dictionary<string, object>
        {
            ["ParsedFields"] = new Dictionary<string, object>()
        });
        var sut = new VariableReadOperator(Substitute.For<ILogger<VariableReadOperator>>(), context);
        var op = new Operator("read", OperatorType.VariableRead, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "tcp.lastResult", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));
        op.AddParameter(TestHelpers.CreateParameter("OutputFieldName", "ParsedFields.Score", "string"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Variable 'tcp.lastResult' field 'ParsedFields.Score' was not found.");
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingRunVariable_ShouldReturnDefaultValueSource()
    {
        var context = new VariableContext();
        var sut = new VariableReadOperator(Substitute.For<ILogger<VariableReadOperator>>(), context);
        var op = new Operator("read", OperatorType.VariableRead, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "threshold.score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DefaultValue", "98.0", "string"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Exists"].Should().Be(false);
        result.OutputData["Value"].Should().Be(98.0);
        result.OutputData["ReadSource"].Should().Be("DefaultValue");
    }

    [Fact]
    public async Task ExecuteAsync_WithFailOnMissingRunVariable_ShouldFailClosed()
    {
        var context = new VariableContext();
        var sut = new VariableReadOperator(Substitute.For<ILogger<VariableReadOperator>>(), context);
        var op = new Operator("read", OperatorType.VariableRead, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "threshold.score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DefaultValue", "98.0", "string"));
        op.AddParameter(TestHelpers.CreateParameter("FailOnMissingVariable", true, "bool"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("threshold.score");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLogRunScopeReadsAtDebugLevel()
    {
        var context = new VariableContext();
        context.SetValue("temperature", 42.5);
        var logger = new RecordingLogger<VariableReadOperator>();
        var sut = new VariableReadOperator(logger, context);
        var op = new Operator("read", OperatorType.VariableRead, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "temperature", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue();
        logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Debug &&
            entry.Message.Contains("[VariableRead]", StringComparison.Ordinal));
        logger.Entries.Should().NotContain(entry =>
            entry.Level == LogLevel.Information &&
            entry.Message.Contains("[VariableRead]", StringComparison.Ordinal));
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

    [Fact]
    public async Task ExecuteAsync_WhenProjectScopeReadsValue_ShouldExposeSnapshotMetadata()
    {
        var variableId = Guid.NewGuid();
        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "tcp.lastScore",
                    DisplayName = "Last TCP Score",
                    ValueType = ProjectGlobalVariableValueType.Double,
                    InitialValue = JsonSerializer.SerializeToElement(0.0)
                }
            ]
        };
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 98.5, ProjectVariableUpdatedBy.VariableWrite, Guid.NewGuid(), Guid.NewGuid());
        var accessor = new ProjectVariableExecutionContextAccessor();
        using var scope = accessor.BeginScope(new ProjectVariableExecutionContext(session, ProjectVariableBindingIndex.Build(schema), Guid.NewGuid()));
        var sut = new VariableReadOperator(Substitute.For<ILogger<VariableReadOperator>>(), new VariableContext(), accessor);
        var op = new Operator("read", OperatorType.VariableRead, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Scope", "Project", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("VariableId", variableId.ToString(), "string"));
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "tcp.lastScore", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Value"].Should().Be(98.5);
        result.OutputData["VariableId"].Should().Be(variableId.ToString("D"));
        result.OutputData["ValueType"].Should().Be(ProjectGlobalVariableValueType.Double.ToString());
        result.OutputData["Version"].Should().Be(1L);
        result.OutputData["UpdatedBy"].Should().Be(ProjectVariableUpdatedBy.VariableWrite.ToString());
        result.OutputData["UpdatedAtUtc"].Should().BeOfType<string>().Which.Should().NotBeNullOrWhiteSpace();
        result.OutputData["ReadSource"].Should().Be("ProjectVariable");
    }
}

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class VariableWriteOperatorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenStaticValueIsMissing_ShouldUseMetadataDefaultZero()
    {
        var context = new VariableContext();
        var sut = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), context);
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "lastCount", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Int", "string"));
        op.AddParameter(TestHelpers.CreateParameter("UseInputValue", false, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        context.GetValue<long>("lastCount").Should().Be(0L);
        result.OutputData!["Value"].Should().Be(0L);
    }

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
    public async Task ExecuteAsync_WithInputFieldName_ShouldWriteNestedValueIntoContext()
    {
        var context = new VariableContext();
        var sut = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), context);
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "lastScore", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));
        op.AddParameter(TestHelpers.CreateParameter("InputFieldName", "ParsedFields.score", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["ParsedFields"] = new Dictionary<string, object>
            {
                ["score"] = 98.5
            }
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        context.GetValue<double>("lastScore").Should().Be(98.5);
        result.OutputData!["Value"].Should().Be(98.5);
    }

    [Fact]
    public async Task ExecuteAsync_WithRunScopeObject_ShouldPreserveStructuredTcpFieldsForLaterReads()
    {
        var context = new VariableContext();
        var writeOperator = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), context);
        var writeOp = new Operator("write", OperatorType.VariableWrite, 0, 0);
        writeOp.AddParameter(TestHelpers.CreateParameter("VariableName", "tcp.lastFields", "string"));
        writeOp.AddParameter(TestHelpers.CreateParameter("DataType", "Object", "string"));
        writeOp.AddParameter(TestHelpers.CreateParameter("InputFieldName", "ParsedFields", "string"));
        var parsedFields = new Dictionary<string, object>
        {
            ["Score"] = 98.5,
            ["Status"] = "OK"
        };

        var writeResult = await writeOperator.ExecuteAsync(writeOp, new Dictionary<string, object>
        {
            ["ParsedFields"] = parsedFields
        });

        writeResult.IsSuccess.Should().BeTrue(writeResult.ErrorMessage);
        writeResult.OutputData!["Value"].Should().BeSameAs(parsedFields);
        context.GetValue<object>("tcp.lastFields").Should().BeSameAs(parsedFields);

        var readOperator = new VariableReadOperator(Substitute.For<ILogger<VariableReadOperator>>(), context);
        var readOp = new Operator("read", OperatorType.VariableRead, 0, 0);
        readOp.AddParameter(TestHelpers.CreateParameter("VariableName", "tcp.lastFields", "string"));
        readOp.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));
        readOp.AddParameter(TestHelpers.CreateParameter("OutputFieldName", "Score", "string"));

        var readResult = await readOperator.ExecuteAsync(readOp);

        readResult.IsSuccess.Should().BeTrue(readResult.ErrorMessage);
        readResult.OutputData!["Value"].Should().Be(98.5);
        readResult.OutputData["RawValue"].Should().BeSameAs(parsedFields);
        readResult.OutputData["ReadSource"].Should().Be("RunVariableField");
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingInputFieldName_ShouldFailWithoutStaticFallback()
    {
        var context = new VariableContext();
        var sut = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), context);
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "lastScore", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));
        op.AddParameter(TestHelpers.CreateParameter("InputFieldName", "ParsedFields.score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("StaticValue", "12.5", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["ParsedFields"] = new Dictionary<string, object>()
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ParsedFields.score");
        context.Contains("lastScore").Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithJsonStringInputFieldAndStatusPath_ShouldWriteIndexedField()
    {
        var context = new VariableContext();
        var sut = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), context);
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "lastScore", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));
        op.AddParameter(TestHelpers.CreateParameter("InputFieldName", "Payload.Results.1.score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RequireInputStatus", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("InputStatusFieldName", "Payload.Status", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Payload"] = """{"Status":"OK","Results":[{"score":97.0},{"score":98.5}]}"""
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        context.GetValue<double>("lastScore").Should().Be(98.5);
        result.OutputData!["Value"].Should().Be(98.5);
        result.OutputData["InputStatusValue"].Should().Be("OK");
        result.OutputData["WriteSkipped"].Should().Be(false);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidRunScopeInputConversion_ShouldFailWithoutWrite()
    {
        var context = new VariableContext();
        var sut = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), context);
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "lastScore", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));
        op.AddParameter(TestHelpers.CreateParameter("InputFieldName", "ParsedFields.score", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["ParsedFields"] = new Dictionary<string, object>
            {
                ["score"] = "not-a-number"
            }
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("lastScore");
        result.ErrorMessage.Should().Contain("Double");
        context.Contains("lastScore").Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidStaticValueConversion_ShouldFailWithoutWrite()
    {
        var context = new VariableContext();
        var sut = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), context);
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "lastCount", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Int", "string"));
        op.AddParameter(TestHelpers.CreateParameter("UseInputValue", false, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("StaticValue", "12.5", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("StaticValue");
        context.Contains("lastCount").Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithRunScopeIntAndFloorConversion_ShouldWriteFlooredValue()
    {
        var context = new VariableContext();
        var sut = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), context);
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "lastCount", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Int", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ConversionMode", "Floor", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = 12.9
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        context.GetValue<long>("lastCount").Should().Be(12L);
        result.OutputData!["Value"].Should().Be(12L);
    }

    [Fact]
    public async Task ExecuteAsync_WithRunScopeIntAndExactFraction_ShouldFailWithoutWrite()
    {
        var context = new VariableContext();
        var sut = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), context);
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "lastCount", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Int", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = 12.9
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("explicit Round, Floor, Ceiling or Truncate");
        context.Contains("lastCount").Should().BeFalse();
    }

    [Fact]
    public void ValidateParameters_WithProjectScopeObjectDataType_ShouldBeInvalid()
    {
        var sut = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), new VariableContext());
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Scope", "Project", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "tcp.lastFields", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Object", "string"));

        var result = sut.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("Run scope", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLogRunScopeWritesAtDebugLevel()
    {
        var context = new VariableContext();
        var logger = new RecordingLogger<VariableWriteOperator>();
        var sut = new VariableWriteOperator(logger, context);
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "batchId", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "String", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = "LAB-001"
        });

        result.IsSuccess.Should().BeTrue();
        logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Debug &&
            entry.Message.Contains("[VariableWrite]", StringComparison.Ordinal));
        logger.Entries.Should().NotContain(entry =>
            entry.Level == LogLevel.Information &&
            entry.Message.Contains("[VariableWrite]", StringComparison.Ordinal));
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

    [Fact]
    public async Task ExecuteAsync_WhenProjectScopeUsesInputFieldName_ShouldWriteParsedFieldValue()
    {
        var variableId = Guid.NewGuid();
        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "tcp.lastCode",
                    DisplayName = "Last TCP Code",
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
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "tcp.lastCode", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Int", "string"));
        op.AddParameter(TestHelpers.CreateParameter("InputFieldName", "ParsedFields.code", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["ParsedFields"] = new Dictionary<string, object>
            {
                ["code"] = 7L
            }
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        session.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(7L);
        result.OutputData!["Value"].Should().Be(7L);
        result.OutputData["VariableId"].Should().Be(variableId.ToString("D"));
        result.OutputData["ValueType"].Should().Be(ProjectGlobalVariableValueType.Int64.ToString());
        result.OutputData["Version"].Should().Be(1L);
        result.OutputData["UpdatedBy"].Should().Be(ProjectVariableUpdatedBy.VariableWrite.ToString());
        result.OutputData["UpdatedAtUtc"].Should().BeOfType<string>().Which.Should().NotBeNullOrWhiteSpace();
    }
    [Fact]
    public async Task ExecuteAsync_WithRequireInputStatusAndFalseStatus_ShouldSkipRunScopeWrite()
    {
        var context = new VariableContext();
        var sut = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), context);
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "lastScore", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));
        op.AddParameter(TestHelpers.CreateParameter("InputFieldName", "ParsedFields.score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RequireInputStatus", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Status"] = false,
            ["ParsedFields"] = new Dictionary<string, object>
            {
                ["score"] = 98.5
            }
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["WriteSkipped"].Should().Be(true);
        result.OutputData["InputStatusValue"].Should().Be("False");
        result.OutputData["SkipReason"].Should().Be("Input status field 'Status' is false.");
        context.Contains("lastScore").Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithRequireInputStatusAndOkText_ShouldWriteProjectParsedFieldValue()
    {
        var variableId = Guid.NewGuid();
        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "tcp.acceptedScore",
                    DisplayName = "Accepted TCP Score",
                    ValueType = ProjectGlobalVariableValueType.Double,
                    InitialValue = JsonSerializer.SerializeToElement(0.0)
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
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "tcp.acceptedScore", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));
        op.AddParameter(TestHelpers.CreateParameter("InputFieldName", "ParsedFields.score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RequireInputStatus", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("InputStatusFieldName", "ResponseAccepted", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["ResponseAccepted"] = "OK",
            ["ParsedFields"] = new Dictionary<string, object>
            {
                ["score"] = 98.5
            }
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        session.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(98.5);
        result.OutputData!["WriteSkipped"].Should().Be(false);
        result.OutputData["InputStatusValue"].Should().Be("OK");
        result.OutputData["Value"].Should().Be(98.5);
    }

    [Fact]
    public async Task ExecuteAsync_WithRequireInputStatusAndFailOnFalse_ShouldFailWithoutWrite()
    {
        var context = new VariableContext();
        var sut = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), context);
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "lastScore", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Double", "string"));
        op.AddParameter(TestHelpers.CreateParameter("InputFieldName", "ParsedFields.score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RequireInputStatus", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("FailOnInputStatusFalse", true, "bool"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Status"] = "NG",
            ["ParsedFields"] = new Dictionary<string, object>
            {
                ["score"] = 98.5
            }
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Input status field 'Status' is false.");
        context.Contains("lastScore").Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithRunScopeBoolAndOkText_ShouldWriteTrue()
    {
        var context = new VariableContext();
        var sut = new VariableWriteOperator(Substitute.For<ILogger<VariableWriteOperator>>(), context);
        var op = new Operator("write", OperatorType.VariableWrite, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "lastOk", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Bool", "string"));
        op.AddParameter(TestHelpers.CreateParameter("InputFieldName", "ParsedFields.status", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["ParsedFields"] = new Dictionary<string, object>
            {
                ["status"] = "OK"
            }
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        context.GetValue<bool>("lastOk").Should().BeTrue();
        result.OutputData!["Value"].Should().Be(true);
    }

    [Fact]
    public async Task ExecuteAsync_WithProjectScopeBooleanAndNgText_ShouldWriteFalse()
    {
        var variableId = Guid.NewGuid();
        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "tcp.lastOk",
                    DisplayName = "Last TCP OK",
                    ValueType = ProjectGlobalVariableValueType.Boolean,
                    InitialValue = JsonSerializer.SerializeToElement(true)
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
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "tcp.lastOk", "string"));
        op.AddParameter(TestHelpers.CreateParameter("DataType", "Bool", "string"));
        op.AddParameter(TestHelpers.CreateParameter("InputFieldName", "ParsedFields.status", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["ParsedFields"] = new Dictionary<string, object>
            {
                ["status"] = "NG"
            }
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        session.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(false);
        result.OutputData!["Value"].Should().Be(false);
        result.OutputData["ValueType"].Should().Be(ProjectGlobalVariableValueType.Boolean.ToString());
    }
}

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class VariableIncrementOperatorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenResetThresholdIsMissing_ShouldUseMetadataDefaultOneHundred()
    {
        var context = new VariableContext();
        context.SetValue("counter", 50L);
        var sut = new VariableIncrementOperator(Substitute.For<ILogger<VariableIncrementOperator>>(), context);
        var op = new Operator("inc", OperatorType.VariableIncrement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "counter", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Delta", 1, "int"));
        op.AddParameter(TestHelpers.CreateParameter("ResetCondition", "GreaterThan", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ResetValue", 0, "int"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["WasReset"].Should().Be(false);
        result.OutputData["NewValue"].Should().Be(51L);
        context.GetValue<long>("counter").Should().Be(51L);
    }

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
    public async Task ExecuteAsync_ShouldLogRunScopeIncrementsAtDebugLevel()
    {
        var context = new VariableContext();
        context.SetValue("counter", 5L);
        var logger = new RecordingLogger<VariableIncrementOperator>();
        var sut = new VariableIncrementOperator(logger, context);
        var op = new Operator("inc", OperatorType.VariableIncrement, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("VariableName", "counter", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Delta", 2, "int"));

        var result = await sut.ExecuteAsync(op);

        result.IsSuccess.Should().BeTrue();
        logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Debug &&
            entry.Message.Contains("[VariableIncrement]", StringComparison.Ordinal));
        logger.Entries.Should().NotContain(entry =>
            entry.Level == LogLevel.Information &&
            entry.Message.Contains("[VariableIncrement]", StringComparison.Ordinal));
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

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
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

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class StringFormatOperatorTests
{
    private readonly StringFormatOperator _operator = new(Substitute.For<ILogger<StringFormatOperator>>());

    [Fact]
    public async Task ExecuteAsync_TemplateMode_ShouldReplaceIndexedAndNamedPlaceholders()
    {
        var op = new Operator("format", OperatorType.StringFormat, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Template", "First={0}; Second={Arg2}; Hidden={Template}", "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Arg1"] = "OK",
            ["Arg2"] = "StationA",
            ["Template"] = "polluted"
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["Result"].Should().Be("First=OK; Second=StationA; Hidden={Template}");
    }

    [Fact]
    public async Task ExecuteAsync_WhenArg1IsMissing_ShouldKeepArg2AtIndexOneAndIgnoreExtraJoinKeys()
    {
        var templateOp = new Operator("format", OperatorType.StringFormat, 0, 0);
        templateOp.AddParameter(TestHelpers.CreateParameter("Template", "{0}|{1}", "string"));
        var templateResult = await _operator.ExecuteAsync(templateOp, new Dictionary<string, object>
        {
            ["Arg2"] = "second",
            ["Template"] = "polluted"
        });

        var joinOp = new Operator("join", OperatorType.StringFormat, 0, 0);
        joinOp.AddParameter(TestHelpers.CreateParameter("Mode", "Join", "string"));
        joinOp.AddParameter(TestHelpers.CreateParameter("Separator", ",", "string"));
        var joinResult = await _operator.ExecuteAsync(joinOp, new Dictionary<string, object>
        {
            ["Arg1"] = "first",
            ["Arg2"] = "second",
            ["Mode"] = "Join",
            ["Separator"] = ","
        });

        templateResult.IsSuccess.Should().BeTrue();
        templateResult.OutputData!["Result"].Should().Be("{0}|second");
        joinResult.IsSuccess.Should().BeTrue();
        joinResult.OutputData!["Result"].Should().Be("first,second");
    }

    [Fact]
    public void Metadata_ShouldDeclareAllRuntimeParametersOutputsAndModeRules()
    {
        var metadata = new OperatorFactory().GetMetadata(OperatorType.StringFormat)!;

        metadata.Parameters.Select(parameter => parameter.Name).Should().Equal(
            "Mode",
            "Template",
            "Separator",
            "DateFormat");
        metadata.OutputPorts.Select(port => (port.Name, port.DataType)).Should().Equal(
            ("Result", PortDataType.String),
            ("Length", PortDataType.Integer),
            ("IsEmpty", PortDataType.Boolean));

        var templateStates = ResolveStates(metadata, "Template");
        templateStates["Template"].EffectiveDisabled.Should().BeFalse();
        templateStates["Separator"].EffectiveDisabled.Should().BeTrue();
        templateStates["DateFormat"].EffectiveDisabled.Should().BeTrue();

        var joinStates = ResolveStates(metadata, "Join");
        joinStates["Template"].EffectiveDisabled.Should().BeTrue();
        joinStates["Separator"].EffectiveDisabled.Should().BeFalse();
        joinStates["DateFormat"].EffectiveDisabled.Should().BeTrue();

        var dateStates = ResolveStates(metadata, "Date");
        dateStates["Template"].EffectiveDisabled.Should().BeTrue();
        dateStates["Separator"].EffectiveDisabled.Should().BeTrue();
        dateStates["DateFormat"].EffectiveDisabled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_TemplateMissingAndExplicitEmpty_ShouldHaveDistinctSemantics()
    {
        var missingTemplate = new Operator("missing-template", OperatorType.StringFormat, 0, 0);
        var missingResult = await _operator.ExecuteAsync(missingTemplate, new Dictionary<string, object>
        {
            ["Arg1"] = "A",
            ["Arg2"] = "B"
        });

        var emptyTemplate = new Operator("empty-template", OperatorType.StringFormat, 0, 0);
        emptyTemplate.AddParameter(TestHelpers.CreateParameter("Template", string.Empty, "string"));
        var emptyResult = await _operator.ExecuteAsync(emptyTemplate, new Dictionary<string, object>
        {
            ["Arg1"] = "A",
            ["Arg2"] = "B"
        });

        missingResult.IsSuccess.Should().BeTrue();
        missingResult.OutputData!["Result"].Should().Be("Result is A and B");
        emptyResult.IsSuccess.Should().BeTrue();
        emptyResult.OutputData!["Result"].Should().Be("AB");
    }

    private static Dictionary<string, OperatorParameterConstraintState> ResolveStates(
        OperatorMetadata metadata,
        string mode) =>
        OperatorParameterConstraintEvaluator
            .ResolveStates(metadata, new Dictionary<string, object?> { ["Mode"] = mode })
            .ToDictionary(state => state.Constraint.Parameter, StringComparer.OrdinalIgnoreCase);
}

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
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
