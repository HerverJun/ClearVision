using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class ResultJudgmentOperatorTests
{
    private readonly ResultJudgmentOperator _operator;

    public ResultJudgmentOperatorTests()
    {
        _operator = new ResultJudgmentOperator(Substitute.For<ILogger<ResultJudgmentOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeResultJudgment()
    {
        _operator.OperatorType.Should().Be(OperatorType.ResultJudgment);
    }

    [Fact]
    public async Task ExecuteAsync_WithNumericEqualWithinTolerance_ShouldReturnOk()
    {
        var op = new Operator("test", OperatorType.ResultJudgment, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Condition", "Equal", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ExpectValue", "10.0", "string"));
        op.AddParameter(TestHelpers.CreateParameter("NumericAbsTolerance", 0.01, "double"));
        op.AddParameter(TestHelpers.CreateParameter("NumericRelTolerance", 0.0, "double"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = 10.005
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["IsOk"].Should().Be(true);
        result.OutputData["JudgmentResult"].Should().Be("OK");
    }

    [Fact]
    public async Task ExecuteAsync_WithLowConfidence_ShouldReturnConfidenceGateNg()
    {
        var op = new Operator("test", OperatorType.ResultJudgment, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinConfidence", 0.8, "double"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = 1,
            ["Confidence"] = 0.5
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["IsOk"].Should().Be(false);
        result.OutputData["Condition"].Should().Be("MinConfidenceGate");
        result.OutputData["Details"].Should().Be("Confidence below MinConfidence");
    }

    [Fact]
    public void ValidateParameters_WithInvalidMinConfidence_ShouldReturnInvalid()
    {
        var op = new Operator("test", OperatorType.ResultJudgment, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("MinConfidence", 1.5, "double"));

        _operator.ValidateParameters(op).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, "true", true)]
    [InlineData(false, "true", false)]
    [InlineData(false, "false", true)]
    public async Task ExecuteAsync_WithBooleanEqual_ShouldUseBooleanSemantics(
        bool actual,
        string expected,
        bool expectedOk)
    {
        var op = CreateJudgment("Equal", expected);

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = actual
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["IsOk"].Should().Be(expectedOk);
    }

    [Theory]
    [InlineData(10.0, true)]
    [InlineData(15.0, true)]
    [InlineData(20.0, true)]
    [InlineData(9.0, false)]
    [InlineData(21.0, false)]
    public async Task ExecuteAsync_WithRange_ShouldIncludeBothBoundaries(double actual, bool expectedOk)
    {
        var op = CreateJudgment("Range", string.Empty);
        op.AddParameter(TestHelpers.CreateParameter("ExpectValueMin", "10", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ExpectValueMax", "20", "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = actual
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["IsOk"].Should().Be(expectedOk);
    }

    [Theory]
    [InlineData("ABC-123", true)]
    [InlineData("abc-123", false)]
    [InlineData("XYZ", false)]
    public async Task ExecuteAsync_WithExpectedCode_ShouldUseExactStringSemantics(string actual, bool expectedOk)
    {
        var op = CreateJudgment("Equal", "ABC-123");

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = actual
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["IsOk"].Should().Be(expectedOk);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingActualValue_ShouldFailClosed()
    {
        var op = CreateJudgment("NotEqual", "forbidden");

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["IsOk"].Should().Be(false);
        result.OutputData["Details"].Should().Be("Actual value is missing.");
    }

    [Fact]
    public async Task ExecuteAsync_WithRequiredButMissingConfidence_ShouldFailClosed()
    {
        var op = CreateJudgment("Equal", "ripe");
        op.AddParameter(TestHelpers.CreateParameter("MinConfidence", 0.8, "double"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = "ripe"
        });

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["IsOk"].Should().Be(false);
        result.OutputData["Condition"].Should().Be("MinConfidenceGate");
        result.OutputData["Details"].Should().Be("Confidence is missing or invalid");
    }

    private static Operator CreateJudgment(string condition, string expected)
    {
        var op = new Operator("test", OperatorType.ResultJudgment, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Condition", condition, "string"));
        op.AddParameter(TestHelpers.CreateParameter("ExpectValue", expected, "string"));
        return op;
    }
}
