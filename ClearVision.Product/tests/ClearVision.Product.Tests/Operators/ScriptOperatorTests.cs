using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
[Trait("Category", "Sprint5_Phase2")]
public class ScriptOperatorTests
{
    [Fact]
    public void OperatorType_ShouldBeScriptOperator()
    {
        var sut = CreateSut();
        Assert.Equal(OperatorType.ScriptOperator, sut.OperatorType);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidExpression_ShouldReturnCalculatedOutputs()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "ScriptLanguage", "CSharpExpression" },
            { "Code", "sum = Input1 + Input2; Output1 = sum; Output2 = sum * 2" },
            { "Timeout", 1000 }
        });

        var inputs = new Dictionary<string, object>
        {
            { "Input1", 2.0 },
            { "Input2", 3.0 }
        };

        var result = await sut.ExecuteAsync(op, inputs);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.OutputData);
        Assert.Equal(5.0, Convert.ToDouble(result.OutputData!["Output1"]), 6);
        Assert.Equal(10.0, Convert.ToDouble(result.OutputData["Output2"]), 6);
    }

    [Fact]
    public void ValidateParameters_WithInvalidLanguage_ShouldReturnInvalid()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "ScriptLanguage", "Python" },
            { "Code", "Output1 = 1" }
        });

        var validation = sut.ValidateParameters(op);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Metadata_ShouldExposeOnlyCSharpExpression()
    {
        var metadata = new OperatorFactory().GetMetadata(OperatorType.ScriptOperator);

        metadata.Should().NotBeNull();
        metadata!.Description.Should().NotContain("脚本片段");
        metadata.Parameters.Single(parameter => parameter.Name == "ScriptLanguage")
            .Options.Should().ContainSingle()
            .Which.Value.Should().Be("CSharpExpression");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPreserveSupportedLiteralVariableArithmeticAndAssignmentSemantics()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["ScriptLanguage"] = "CSharpExpression",
            ["Code"] = "copy = Input1; total = copy + 2.5; Output1 = total; Output2 = 'accepted'"
        });

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Input1"] = 4.5d });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToDouble(result.OutputData!["Output1"]).Should().Be(7d);
        result.OutputData["Output2"].Should().Be("accepted");
    }

    [Theory]
    [InlineData("CSharpScript", ScriptOperator.UnsupportedLanguageCode)]
    [InlineData("csharpexpression", ScriptOperator.InvalidLanguageCode)]
    [InlineData("Python", ScriptOperator.InvalidLanguageCode)]
    [InlineData("", ScriptOperator.InvalidLanguageCode)]
    public async Task ValidatorAndDirectExecute_ShouldRejectUnsupportedOrInvalidLanguage(
        string language,
        string expectedCode)
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["ScriptLanguage"] = language,
            ["Code"] = "Output1 = 1"
        });

        var validation = sut.ValidateParameters(op);
        var result = await sut.ExecuteAsync(op);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().StartWith(expectedCode + ":");
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith(expectedCode + ":");
    }

    [Theory]
    [InlineData("Output1 = Math.Sqrt(Input1)", ScriptOperator.UnsupportedFunctionCode)]
    [InlineData("Output1 = Input1 +", ScriptOperator.InvalidExpressionCode)]
    [InlineData("Output1 = MissingValue + 1", ScriptOperator.UnresolvedVariableCode)]
    [InlineData("1invalid = Input1", ScriptOperator.InvalidAssignmentCode)]
    [InlineData("Output1 =", ScriptOperator.InvalidAssignmentCode)]
    public async Task ValidatorAndDirectExecute_ShouldFailClosedForInvalidExpressions(
        string code,
        string expectedCode)
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            ["ScriptLanguage"] = "CSharpExpression",
            ["Code"] = code
        });

        var validation = sut.ValidateParameters(op);
        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Input1"] = 9d });

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().StartWith(expectedCode + ":");
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().StartWith(expectedCode + ":");
        result.OutputData.Should().BeNull();
    }

    private static ScriptOperator CreateSut()
    {
        return new ScriptOperator(Substitute.For<ILogger<ScriptOperator>>());
    }

    private static Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        var op = new Operator("Script", OperatorType.ScriptOperator, 0, 0);

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
