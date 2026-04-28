using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Acme.Product.Tests.Operators;

public class TimerStatisticsOperatorTests
{
    private readonly TimerStatisticsOperator _operator;

    public TimerStatisticsOperatorTests()
    {
        _operator = new TimerStatisticsOperator(Substitute.For<ILogger<TimerStatisticsOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeTimerStatistics()
    {
        Assert.Equal(OperatorType.TimerStatistics, _operator.OperatorType);
    }

    [Fact]
    public async Task ExecuteAsync_WithCumulativeMode_ShouldAccumulate()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "Mode", "Cumulative" },
            { "ResetInterval", 0 }
        });

        await _operator.ExecuteAsync(op, null);
        await Task.Delay(25);
        var result = await _operator.ExecuteAsync(op, null);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputData);
        Assert.Equal(2, (int)result.OutputData!["Count"]);
        Assert.True(Convert.ToDouble(result.OutputData["TotalMs"]) >= Convert.ToDouble(result.OutputData["AverageMs"]));
        Assert.Equal("OperatorInstance", result.OutputData["StateScope"]);
        Assert.Equal(120, (int)result.OutputData["StateTtlMinutes"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithDifferentOperatorIds_ShouldKeepIndependentState()
    {
        var opA = CreateOperator(Guid.NewGuid(), new Dictionary<string, object> { { "Mode", "Cumulative" } });
        var opB = CreateOperator(Guid.NewGuid(), new Dictionary<string, object> { { "Mode", "Cumulative" } });

        await _operator.ExecuteAsync(opA, null);
        await _operator.ExecuteAsync(opA, null);
        var resultB = await _operator.ExecuteAsync(opB, null);

        Assert.True(resultB.IsSuccess);
        Assert.Equal(1, (int)resultB.OutputData!["Count"]);
        Assert.Equal(opB.Id, Assert.IsType<Guid>(resultB.OutputData["StateKey"]));
    }

    [Fact]
    public async Task ExecuteAsync_WithReset_ShouldClearCumulativeState()
    {
        var operatorId = Guid.NewGuid();
        var op = CreateOperator(operatorId, new Dictionary<string, object> { { "Mode", "Cumulative" } });

        await _operator.ExecuteAsync(op, null);
        await _operator.ExecuteAsync(op, null);

        var resetOp = CreateOperator(operatorId, new Dictionary<string, object>
        {
            { "Mode", "Cumulative" },
            { "Reset", true }
        });
        var resetResult = await _operator.ExecuteAsync(resetOp, null);
        var afterReset = await _operator.ExecuteAsync(op, null);

        Assert.True(resetResult.IsSuccess);
        Assert.Equal(0, (int)resetResult.OutputData!["Count"]);
        Assert.Equal(0.0, Convert.ToDouble(resetResult.OutputData["TotalMs"]));
        Assert.True((bool)resetResult.OutputData["ResetApplied"]);
        Assert.Equal(1, (int)afterReset.OutputData!["Count"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithExpiredTtl_ShouldDropOldOperatorState()
    {
        var opA = CreateOperator(Guid.NewGuid(), new Dictionary<string, object>
        {
            { "Mode", "Cumulative" },
            { "StateTtlMinutes", 0 }
        });
        var opB = CreateOperator(Guid.NewGuid(), new Dictionary<string, object> { { "Mode", "Cumulative" } });

        await _operator.ExecuteAsync(opA, null);
        await Task.Delay(5);
        await _operator.ExecuteAsync(opB, null);
        var resultA = await _operator.ExecuteAsync(opA, null);

        Assert.True(resultA.IsSuccess);
        Assert.Equal(1, (int)resultA.OutputData!["Count"]);
    }

    [Fact]
    public void ValidateParameters_WithInvalidMode_ShouldReturnInvalid()
    {
        var op = CreateOperator(new Dictionary<string, object> { { "Mode", "Invalid" } });

        var validation = _operator.ValidateParameters(op);

        Assert.False(validation.IsValid);
    }

    private static Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        return CreateOperator(Guid.NewGuid(), parameters);
    }

    private static Operator CreateOperator(Guid id, Dictionary<string, object>? parameters = null)
    {
        var op = new Operator(id, "Timer", OperatorType.TimerStatistics, 0, 0);

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
