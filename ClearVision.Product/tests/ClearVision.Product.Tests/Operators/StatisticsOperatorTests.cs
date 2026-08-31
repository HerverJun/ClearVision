using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class StatisticsOperatorTests : IDisposable
{
    private readonly StatisticsOperator _operator;
    private readonly IDisposable _authorityScope;

    public StatisticsOperatorTests()
    {
        _authorityScope = TestExecutionAuthorityScope.Enter();
        _operator = new StatisticsOperator(Substitute.For<ILogger<StatisticsOperator>>());
    }

    public void Dispose() => _authorityScope.Dispose();

    [Fact]
    public async Task ExecuteAsync_BasicStats_ReturnsCorrectResults()
    {
        var op = CreateOperator(Guid.NewGuid());
        await _operator.ExecuteAsync(op, new Dictionary<string, object> { { "Value", 10.0 } });
        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object> { { "Value", 20.0 } });

        Assert.True(result.IsSuccess);
        Assert.Equal(15.0, (double)result.OutputData!["Mean"]);
        Assert.Equal(2, (int)result.OutputData["Count"]);
        Assert.Equal(Math.Sqrt(50), (double)result.OutputData["StdDev"], 3);
    }

    [Fact]
    public async Task ExecuteAsync_WithCpkParams_CalculatesCpkCorrectly()
    {
        var op = CreateOperator(
            Guid.NewGuid(),
            new Dictionary<string, object>
            {
                { "USL", 13.0 },
                { "LSL", 7.0 }
            });

        var offset = Math.Sqrt(0.5);
        await _operator.ExecuteAsync(op, new Dictionary<string, object> { { "Value", 10.0 - offset } });
        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object> { { "Value", 10.0 + offset } });

        Assert.True(result.IsSuccess);
        Assert.Equal(1.0, (double)result.OutputData!["Cp"], 2);
        Assert.Equal(1.0, (double)result.OutputData["Cpk"], 2);
    }

    [Fact]
    public async Task ExecuteAsync_DifferentOperators_ShouldKeepIndependentHistory()
    {
        var opA = CreateOperator(Guid.NewGuid());
        var opB = CreateOperator(Guid.NewGuid());

        await _operator.ExecuteAsync(opA, new Dictionary<string, object> { { "Value", 10.0 } });
        var resultB = await _operator.ExecuteAsync(opB, new Dictionary<string, object> { { "Value", 20.0 } });
        var resultA = await _operator.ExecuteAsync(opA, new Dictionary<string, object> { { "Value", 30.0 } });

        Assert.Equal(1, (int)resultB.OutputData!["Count"]);
        Assert.Equal(2, (int)resultA.OutputData!["Count"]);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWindowTrimsOldValues_ShouldMaintainRollingStats()
    {
        var op = CreateOperator(
            Guid.NewGuid(),
            new Dictionary<string, object>
            {
                ["WindowSize"] = 3
            });

        await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Value"] = 1.0 });
        await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Value"] = 10.0 });
        await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Value"] = 2.0 });
        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Value"] = 3.0 });

        Assert.True(result.IsSuccess);
        Assert.Equal(3, (int)result.OutputData!["Count"]);
        Assert.Equal(5.0, (double)result.OutputData["Mean"]);
        Assert.Equal(2.0, (double)result.OutputData["Min"]);
        Assert.Equal(10.0, (double)result.OutputData["Max"]);
        Assert.Equal(8.0, (double)result.OutputData["Range"]);
        Assert.Equal(Math.Sqrt(19.0), (double)result.OutputData["StdDev"], 3);
    }

    [Fact]
    public async Task ExecuteAsync_WithReset_ShouldClearRollingHistoryBeforeAddingValue()
    {
        var op = CreateOperator(Guid.NewGuid());
        await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Value"] = 10.0 });
        await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Value"] = 20.0 });
        op = CreateOperator(
            op.Id,
            new Dictionary<string, object>
            {
                ["Reset"] = true
            });

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object> { ["Value"] = 30.0 });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, (int)result.OutputData!["Count"]);
        Assert.Equal(30.0, (double)result.OutputData["Mean"]);
        Assert.Equal(30.0, (double)result.OutputData["Min"]);
        Assert.Equal(30.0, (double)result.OutputData["Max"]);
    }


    [Fact]
    public void ValidateParameters_UslLessThanLsl_ReturnsInvalid()
    {
        var op = CreateOperator(
            Guid.NewGuid(),
            new Dictionary<string, object>
            {
                { "USL", 5.0 },
                { "LSL", 10.0 }
            });

        var result = _operator.ValidateParameters(op);

        Assert.False(result.IsValid);
    }

    private static Operator CreateOperator(Guid id, Dictionary<string, object>? parameters = null)
    {
        var op = new Operator(id, "Stats", OperatorType.Statistics, 0, 0);
        var effectiveParameters = new Dictionary<string, object>
        {
            ["WindowSize"] = 1000,
            ["Reset"] = false
        };

        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
            {
                effectiveParameters[key] = value;
            }
        }

        foreach (var (key, value) in effectiveParameters)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), key, key, "", GetParameterDataType(value), value));
        }

        return op;
    }

    private static string GetParameterDataType(object value)
    {
        return value switch
        {
            bool => "bool",
            int or long => "int",
            double or float or decimal => "double",
            _ => "string"
        };
    }
}
