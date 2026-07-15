using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class MeanFilterOperatorTests
{
    private readonly MeanFilterOperator _operator;

    public MeanFilterOperatorTests()
    {
        _operator = new MeanFilterOperator(Substitute.For<ILogger<MeanFilterOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeMeanFilter()
    {
        Assert.Equal(OperatorType.MeanFilter, _operator.OperatorType);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullInputs_ShouldReturnFailure()
    {
        var op = CreateOperator();
        var result = await _operator.ExecuteAsync(op, null);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidImage_ShouldReturnSuccess()
    {
        var op = CreateOperator();
        using var image = TestHelpers.CreateTestImage();
        var inputs = TestHelpers.CreateImageInputs(image);

        var result = await _operator.ExecuteAsync(op, inputs);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputData);
        Assert.True(result.OutputData!.ContainsKey("Image"));
    }

    [Fact]
    public void ValidateParameters_WithOutOfRangeKernel_ShouldReturnInvalid()
    {
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "KernelSize", 0 }
        });

        var validation = _operator.ValidateParameters(op);

        Assert.False(validation.IsValid);
    }

    private static Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        var op = new Operator("MeanFilter", OperatorType.MeanFilter, 0, 0);

        if (parameters == null)
        {
            return op;
        }

        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, "string", value));
        }

        return op;
    }
}
