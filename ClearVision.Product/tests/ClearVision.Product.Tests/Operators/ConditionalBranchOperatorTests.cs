// ConditionalBranchOperatorTests.cs
// ConditionalBranchOperatorTests测试
// 作者：蘅芜君

using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Operators;

public class ConditionalBranchOperatorTests
{
    private readonly ConditionalBranchOperator _operator;

    public ConditionalBranchOperatorTests()
    {
        _operator = new ConditionalBranchOperator(Substitute.For<ILogger<ConditionalBranchOperator>>());
    }

    [Fact]
    public void OperatorType_ShouldBeConditionalBranch()
    {
        _operator.OperatorType.Should().Be(OperatorType.ConditionalBranch);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullInputs_ShouldReturnFailure()
    {
        var op = new Operator("测试", OperatorType.ConditionalBranch, 0, 0);
        var result = await _operator.ExecuteAsync(op, null);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithValidValue_ShouldReturnSuccess()
    {
        var op = new Operator("测试", OperatorType.ConditionalBranch, 0, 0);
        var inputs = new Dictionary<string, object> { { "Value", 42.0 } };
        var result = await _operator.ExecuteAsync(op, inputs);
        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().ContainKey("Result");
    }

    [Fact]
    public async Task ExecuteAsync_WithNestedFieldName_ShouldEvaluateExpandedNumericCondition()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Measurement.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "GreaterThanOrEqual", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "98.5", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Measurement"] = new Dictionary<string, object>
            {
                ["Score"] = 98.5
            }
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["ActualValue"].Should().Be(98.5);
        result.OutputData["ActualSource"].Should().Be("Field");
        result.OutputData["True"].Should().BeSameAs(payload);
        result.OutputData["False"].Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithJsonStringFieldName_ShouldEvaluateNestedField()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Measurement.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "GreaterThanOrEqual", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "98.5", "string"));
        var payload = """{"Measurement":{"Score":98.5,"Status":"OK"}}""";

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["ActualValue"].Should().Be(98.5);
        result.OutputData["ActualSource"].Should().Be("Field");
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithJsonArrayIndexFieldName_ShouldEvaluateIndexedField()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Measurements.1.Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "Equal", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "OK", "string"));
        var payload = """{"Measurements":[{"Status":"NG"},{"Status":"OK"}]}""";

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["ActualValue"].Should().Be("OK");
        result.OutputData["ActualSource"].Should().Be("Field");
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithBracketJsonArrayIndexFieldName_ShouldEvaluateIndexedField()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Measurements[1].Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "Equal", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "OK", "string"));
        var payload = """{"Measurements":[{"Status":"NG"},{"Status":"OK"}]}""";

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["ActualValue"].Should().Be("OK");
        result.OutputData["ActualSource"].Should().Be("Field");
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithNumericToleranceAndEqual_ShouldAcceptSmallMeasurementDrift()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Measurement.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "Equal", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "98.5", "string"));
        op.AddParameter(TestHelpers.CreateParameter("NumericTolerance", 0.001, "double"));
        var payload = new Dictionary<string, object>
        {
            ["Measurement"] = new Dictionary<string, object>
            {
                ["Score"] = 98.5004
            }
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["NumericTolerance"].Should().Be(0.001);
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithNumericToleranceAndNotEqual_ShouldRejectValuesOutsideTolerance()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Measurement.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "NotEqual", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "98.5", "string"));
        op.AddParameter(TestHelpers.CreateParameter("NumericTolerance", 0.001, "double"));
        var payload = new Dictionary<string, object>
        {
            ["Measurement"] = new Dictionary<string, object>
            {
                ["Score"] = 98.502
            }
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingFieldNameAndDefaultPolicy_ShouldUseInputValueFallback()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Parsed.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "IsNotEmpty", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Status"] = "OK"
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["ActualValue"].Should().BeSameAs(payload);
        result.OutputData["ActualSource"].Should().Be("ValueFallback");
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingFieldNameAndFailOnMissingField_ShouldFailClosed()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Parsed.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("FailOnMissingField", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "GreaterThanOrEqual", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "98", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Status"] = "OK"
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Parsed.Score");
    }

    [Fact]
    public async Task ExecuteAsync_WithCompareFieldName_ShouldCompareAgainstUpstreamValue()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Parsed.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareFieldName", "Threshold.Value", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "999", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "GreaterThanOrEqual", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Parsed"] = new Dictionary<string, object>
            {
                ["Score"] = 98.5
            },
            ["Threshold"] = new Dictionary<string, object>
            {
                ["Value"] = 98.0
            }
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["CompareValue"].Should().Be("98");
        result.OutputData["CompareFieldName"].Should().Be("Threshold.Value");
        result.OutputData["CompareSource"].Should().Be("Field");
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithCompareInput_ShouldPreferDynamicInputOverStaticValue()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Parsed.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "999", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "GreaterThanOrEqual", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Parsed"] = new Dictionary<string, object>
            {
                ["Score"] = 98.5
            }
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload,
            ["Compare"] = 98.0
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["CompareValue"].Should().Be("98");
        result.OutputData["CompareSource"].Should().Be("Input");
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithCompareInputFieldName_ShouldReadVariableReadStyleOutput()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Parsed.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareFieldName", "Value", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "999", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "GreaterThanOrEqual", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Parsed"] = new Dictionary<string, object>
            {
                ["Score"] = 98.5
            }
        };
        var variableReadOutput = new Dictionary<string, object>
        {
            ["Value"] = 98.0,
            ["VariableId"] = "threshold.score",
            ["Version"] = 3
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload,
            ["Compare"] = variableReadOutput
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["CompareValue"].Should().Be("98");
        result.OutputData["CompareFieldName"].Should().Be("Value");
        result.OutputData["CompareSource"].Should().Be("InputField");
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingCompareFieldName_ShouldFailClosed()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Parsed.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareFieldName", "Threshold.Value", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "0", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "GreaterThanOrEqual", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Parsed"] = new Dictionary<string, object>
            {
                ["Score"] = 98.5
            }
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Threshold.Value");
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingCompareInputFieldName_ShouldFailClosed()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Parsed.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareFieldName", "Value", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "0", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "GreaterThanOrEqual", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Parsed"] = new Dictionary<string, object>
            {
                ["Score"] = 98.5
            }
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload,
            ["Compare"] = new Dictionary<string, object>
            {
                ["Version"] = 3
            }
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Compare input field");
        result.ErrorMessage.Should().Contain("Value");
    }

    [Fact]
    public async Task ExecuteAsync_WithNgTextAndIsFalse_ShouldRouteTrueBranch()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "IsFalse", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Status"] = "NG"
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithStaticInRange_ShouldRouteTrueBranch()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Parsed.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "InRange", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RangeMin", 98.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("RangeMax", 99.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("NumericTolerance", 0.01, "double"));
        var payload = new Dictionary<string, object>
        {
            ["Parsed"] = new Dictionary<string, object>
            {
                ["Score"] = 97.995
            }
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["RangeMin"].Should().Be(98.0);
        result.OutputData["RangeMax"].Should().Be(99.0);
        result.OutputData["RangeSource"].Should().Be("Parameters");
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithDynamicRangeFromCompareInput_ShouldUseRangeBounds()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Parsed.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareFieldName", "Value", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "InRange", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RangeMin", 0.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("RangeMax", 1.0, "double"));
        var payload = new Dictionary<string, object>
        {
            ["Parsed"] = new Dictionary<string, object>
            {
                ["Score"] = 98.5
            }
        };
        var variableReadOutput = new Dictionary<string, object>
        {
            ["Value"] = new Dictionary<string, object>
            {
                ["Min"] = 98.0,
                ["Max"] = 99.0
            },
            ["VariableId"] = "threshold.score.range",
            ["Version"] = 4
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload,
            ["Compare"] = variableReadOutput
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["CompareSource"].Should().Be("InputField");
        result.OutputData["RangeSource"].Should().Be("CompareValue");
        result.OutputData["RangeMin"].Should().Be(98.0);
        result.OutputData["RangeMax"].Should().Be(99.0);
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithNotInRange_ShouldRouteTrueBranchWhenOutsideLimits()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Parsed.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "NotInRange", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RangeMin", 98.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("RangeMax", 99.0, "double"));
        var payload = new Dictionary<string, object>
        {
            ["Parsed"] = new Dictionary<string, object>
            {
                ["Score"] = 101.0
            }
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithDefaultTextComparison_ShouldRemainCaseSensitive()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "Equal", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "OK", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Status"] = "ok"
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(false);
        result.OutputData["IgnoreCase"].Should().Be(false);
        result.OutputData["False"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithIgnoreCaseTextComparison_ShouldRouteTrueBranch()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "Contains", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "ACK:OK", "string"));
        op.AddParameter(TestHelpers.CreateParameter("IgnoreCase", true, "bool"));
        var payload = new Dictionary<string, object>
        {
            ["Status"] = "ack:ok;score=98.5"
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["IgnoreCase"].Should().Be(true);
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithIgnoreCaseRegex_ShouldRouteTrueBranch()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "Matches", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "^ack:(ok|pass)$", "string"));
        op.AddParameter(TestHelpers.CreateParameter("IgnoreCase", true, "bool"));
        var payload = new Dictionary<string, object>
        {
            ["Status"] = "ACK:PASS"
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithInListAndIgnoreCase_ShouldRouteTrueBranch()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "InList", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "OK,PASS,READY", "string"));
        op.AddParameter(TestHelpers.CreateParameter("IgnoreCase", true, "bool"));
        var payload = new Dictionary<string, object>
        {
            ["Status"] = "pass"
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["CompareListDelimiter"].Should().Be(",");
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithAdditionalCompareListDelimiters_ShouldRouteTrueBranch()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "InList", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "OK;PASS,READY", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareListDelimiter", ",", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareListDelimiters", ";", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Status"] = "PASS"
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["CompareListDelimiter"].Should().Be(",");
        result.OutputData["CompareListDelimiters"].Should().Be(";");
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithInListJsonArrayCompareValue_ShouldRouteTrueBranch()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Status", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "InList", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", """["OK","PASS","READY"]""", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Status"] = "PASS"
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithNotInListAndDynamicCompareInput_ShouldRouteTrueBranch()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Parsed.Code", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareFieldName", "Value", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "NotInList", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareListDelimiter", ";", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Parsed"] = new Dictionary<string, object>
            {
                ["Code"] = "E05"
            }
        };
        var allowedCodes = new Dictionary<string, object>
        {
            ["Value"] = "OK;PASS;READY"
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload,
            ["Compare"] = allowedCodes
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["CompareSource"].Should().Be("InputField");
        result.OutputData["CompareListDelimiter"].Should().Be(";");
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithInListNumericTolerance_ShouldAcceptEquivalentValue()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Parsed.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "InList", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "97.5,98.5,99.5", "string"));
        op.AddParameter(TestHelpers.CreateParameter("NumericTolerance", 0.001, "double"));
        var payload = new Dictionary<string, object>
        {
            ["Parsed"] = new Dictionary<string, object>
            {
                ["Score"] = 98.5004
            }
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithRangeJsonObjectCompareValue_ShouldUseConfiguredBounds()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("FieldName", "Parsed.Score", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Condition", "InRange", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", """{"Min":98.0,"Max":99.0}""", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Parsed"] = new Dictionary<string, object>
            {
                ["Score"] = 98.5
            }
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(true);
        result.OutputData["RangeMin"].Should().Be(98.0);
        result.OutputData["RangeMax"].Should().Be(99.0);
        result.OutputData["RangeSource"].Should().Be("CompareValue");
        result.OutputData["True"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonNumericGreaterThan_ShouldRouteFalseAndExposeEvaluationError()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Condition", "GreaterThan", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "98.5", "string"));
        var payload = new Dictionary<string, object>
        {
            ["Score"] = "unreadable"
        };

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = payload
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(false);
        result.OutputData["EvaluationSuccess"].Should().Be(false);
        result.OutputData["EvaluationError"].Should().Be("Condition 'GreaterThan' requires numeric ActualValue and CompareValue.");
        result.OutputData["True"].Should().BeNull();
        result.OutputData["False"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithStrictEvaluationError_ShouldReturnFailure()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Condition", "GreaterThan", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "98.5", "string"));
        op.AddParameter(TestHelpers.CreateParameter("FailOnEvaluationError", true, "bool"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = "not-a-number"
        });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Condition 'GreaterThan' requires numeric ActualValue and CompareValue.");
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidRegexCondition_ShouldExposeEvaluationError()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Condition", "Matches", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareValue", "(", "string"));

        var result = await _operator.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Value"] = "OK-123"
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData!["Result"].Should().Be(false);
        result.OutputData["EvaluationSuccess"].Should().Be(false);
        result.OutputData["EvaluationError"].Should().BeAssignableTo<string>().Which.Should().Contain("Condition 'Matches' regex is invalid");
    }

    [Fact]
    public void ValidateParameters_Default_ShouldBeValid()
    {
        var op = new Operator("测试", OperatorType.ConditionalBranch, 0, 0);
        _operator.ValidateParameters(op).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithInvalidRange_ShouldBeInvalid()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Condition", "InRange", "string"));
        op.AddParameter(TestHelpers.CreateParameter("RangeMin", 99.0, "double"));
        op.AddParameter(TestHelpers.CreateParameter("RangeMax", 98.0, "double"));

        _operator.ValidateParameters(op).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateParameters_WithInListAndEmptyDelimiter_ShouldBeInvalid()
    {
        var op = new Operator("branch", OperatorType.ConditionalBranch, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Condition", "InList", "string"));
        op.AddParameter(TestHelpers.CreateParameter("CompareListDelimiter", string.Empty, "string"));

        _operator.ValidateParameters(op).IsValid.Should().BeFalse();
    }
}
