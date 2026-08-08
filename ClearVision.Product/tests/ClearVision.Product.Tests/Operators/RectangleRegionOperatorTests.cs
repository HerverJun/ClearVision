using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class RectangleRegionOperatorTests
{
    private readonly RectangleRegionOperator _operator;

    public RectangleRegionOperatorTests()
    {
        _operator = new RectangleRegionOperator(Substitute.For<ILogger<RectangleRegionOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeRectangleRegion()
    {
        _operator.OperatorType.Should().Be(OperatorType.RectangleRegion);
    }

    [Fact]
    public async Task ExecuteAsync_WithRectangleParameters_ShouldEmitRectangleDictionary()
    {
        var op = CreateOperator(12, 14, 40, 22);

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().ContainKey("Rectangle");

        var rectangle = result.OutputData!["Rectangle"].Should().BeAssignableTo<IDictionary<string, object>>().Subject;
        Convert.ToInt32(rectangle["X"]).Should().Be(12);
        Convert.ToInt32(rectangle["Y"]).Should().Be(14);
        Convert.ToInt32(rectangle["Width"]).Should().Be(40);
        Convert.ToInt32(rectangle["Height"]).Should().Be(22);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    public void ValidateParameters_WithDegenerateSize_ShouldReject(int width, int height)
    {
        var op = CreateOperator(0, 0, width, height);

        var result = _operator.ValidateParameters(op);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("Width and Height"));
    }

    private static Operator CreateOperator(int x, int y, int width, int height)
    {
        var op = new Operator("Rectangle Region", OperatorType.RectangleRegion, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("X", x, "int"));
        op.AddParameter(TestHelpers.CreateParameter("Y", y, "int"));
        op.AddParameter(TestHelpers.CreateParameter("Width", width, "int"));
        op.AddParameter(TestHelpers.CreateParameter("Height", height, "int"));
        return op;
    }
}
