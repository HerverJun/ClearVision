using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.ValueObjects;
using FluentAssertions;

namespace ClearVision.Product.Tests.ProjectVariables;

public sealed class ProjectGlobalVariableSchemaTests
{
    [Fact]
    public void Validate_WhenVariableNamesDuplicate_ReturnsDiagnostic()
    {
        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                Variable("judge.expected_count", ProjectGlobalVariableValueType.Int64, 4),
                Variable("judge.expected_count", ProjectGlobalVariableValueType.Int64, 5)
            ]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema);

        diagnostics.Should().Contain(item => item.Code == "GV004");
    }

    [Fact]
    public void Validate_WhenBindingReferencesFlowMembers_ReturnsNoErrors()
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var targetParameterId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Count", PortDataType.Integer);
        var target = new Operator(Guid.NewGuid(), "Target", OperatorType.ResultJudgment, 10, 0);
        target.AddParameter(new Parameter(targetParameterId, "ExpectedCount", "ExpectedCount", "", "int", 0));
        var flow = new OperatorFlow("flow");
        flow.AddOperator(source);
        flow.AddOperator(target);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables = [Variable("last.detected_count", ProjectGlobalVariableValueType.Int64, 0, variableId)],
            SourceBindings =
            [
                new ProjectGlobalVariableSourceBinding
                {
                    Id = Guid.NewGuid(),
                    VariableId = variableId,
                    OperatorId = source.Id,
                    OutputPortId = sourcePortId,
                    OperatorName = source.Name,
                    OutputPortName = "Count"
                }
            ],
            TargetBindings =
            [
                new ProjectGlobalVariableTargetBinding
                {
                    Id = Guid.NewGuid(),
                    VariableId = variableId,
                    OperatorId = target.Id,
                    ParameterId = targetParameterId,
                    OperatorName = target.Name,
                    ParameterName = "ExpectedCount"
                }
            ]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema, flow);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WhenStringVariableTargetsNumericParameter_ReturnsDiagnostic()
    {
        var variableId = Guid.NewGuid();
        var parameterId = Guid.NewGuid();
        var target = new Operator(Guid.NewGuid(), "Target", OperatorType.ResultJudgment, 10, 0);
        target.AddParameter(new Parameter(parameterId, "ExpectedCount", "ExpectedCount", "", "int", 0));
        var flow = new OperatorFlow("flow");
        flow.AddOperator(target);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables = [Variable("judge.expected_count", ProjectGlobalVariableValueType.String, "4", variableId)],
            TargetBindings =
            [
                new ProjectGlobalVariableTargetBinding
                {
                    Id = Guid.NewGuid(),
                    VariableId = variableId,
                    OperatorId = target.Id,
                    ParameterId = parameterId,
                    OperatorName = target.Name,
                    ParameterName = "ExpectedCount"
                }
            ]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema, flow);

        diagnostics.Should().Contain(item => item.Code == "GV022");
    }

    [Fact]
    public void Validate_WhenDoubleVariableTargetsIntegerParameterWithoutExplicitConversion_ReturnsGv022()
    {
        var variableId = Guid.NewGuid();
        var parameterId = Guid.NewGuid();
        var target = new Operator(Guid.NewGuid(), "Target", OperatorType.ResultJudgment, 10, 0);
        target.AddParameter(new Parameter(parameterId, "ExpectedCount", "ExpectedCount", "", "int", 0));
        var flow = new OperatorFlow("flow");
        flow.AddOperator(target);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables = [Variable("stats.score", ProjectGlobalVariableValueType.Double, 4.25, variableId)],
            TargetBindings =
            [
                new ProjectGlobalVariableTargetBinding
                {
                    Id = Guid.NewGuid(),
                    VariableId = variableId,
                    OperatorId = target.Id,
                    ParameterId = parameterId,
                    OperatorName = target.Name,
                    ParameterName = "ExpectedCount"
                }
            ]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema, flow);

        diagnostics.Should().Contain(item => item.Code == "GV022");
    }

    [Fact]
    public void Validate_WhenDoubleVariableTargetsIntegerParameterWithExplicitConversion_ReturnsNoErrors()
    {
        var variableId = Guid.NewGuid();
        var parameterId = Guid.NewGuid();
        var target = new Operator(Guid.NewGuid(), "Target", OperatorType.ResultJudgment, 10, 0);
        target.AddParameter(new Parameter(parameterId, "ExpectedCount", "ExpectedCount", "", "int", 0));
        var flow = new OperatorFlow("flow");
        flow.AddOperator(target);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables = [Variable("stats.score", ProjectGlobalVariableValueType.Double, 4.25, variableId)],
            TargetBindings =
            [
                new ProjectGlobalVariableTargetBinding
                {
                    Id = Guid.NewGuid(),
                    VariableId = variableId,
                    OperatorId = target.Id,
                    ParameterId = parameterId,
                    OperatorName = target.Name,
                    ParameterName = "ExpectedCount",
                    ConversionMode = ProjectVariableConversionMode.Floor
                }
            ]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema, flow);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WhenImageOutputIsBoundToVariable_ReturnsDiagnostic()
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.ImageAcquisition, 0, 0);
        source.LoadOutputPort(sourcePortId, "Image", PortDataType.Image);
        var flow = new OperatorFlow("flow");
        flow.AddOperator(source);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables = [Variable("last.image", ProjectGlobalVariableValueType.String, "", variableId)],
            SourceBindings =
            [
                new ProjectGlobalVariableSourceBinding
                {
                    Id = Guid.NewGuid(),
                    VariableId = variableId,
                    OperatorId = source.Id,
                    OutputPortId = sourcePortId,
                    OperatorName = source.Name,
                    OutputPortName = "Image"
                }
            ]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema, flow);

        diagnostics.Should().Contain(item => item.Code == "GV017");
    }

    [Fact]
    public void Validate_WhenSourceAndTargetBindingsCreateImplicitCycle_ReturnsGv024()
    {
        var variableA = Guid.NewGuid();
        var variableB = Guid.NewGuid();
        var portA = Guid.NewGuid();
        var portB = Guid.NewGuid();
        var paramA = Guid.NewGuid();
        var paramB = Guid.NewGuid();
        var opA = new Operator(Guid.NewGuid(), "OperatorA", OperatorType.ResultJudgment, 0, 0);
        opA.LoadOutputPort(portA, "OutA", PortDataType.Integer);
        opA.AddParameter(new Parameter(paramA, "InA", "InA", "", "int", 0));
        var opB = new Operator(Guid.NewGuid(), "OperatorB", OperatorType.ResultJudgment, 10, 0);
        opB.LoadOutputPort(portB, "OutB", PortDataType.Integer);
        opB.AddParameter(new Parameter(paramB, "InB", "InB", "", "int", 0));
        var flow = new OperatorFlow("implicit-cycle");
        flow.AddOperator(opA);
        flow.AddOperator(opB);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                Variable("stats.a", ProjectGlobalVariableValueType.Int64, 0, variableA),
                Variable("stats.b", ProjectGlobalVariableValueType.Int64, 0, variableB)
            ],
            SourceBindings =
            [
                new ProjectGlobalVariableSourceBinding { Id = Guid.NewGuid(), VariableId = variableA, OperatorId = opA.Id, OutputPortId = portA, OperatorName = opA.Name, OutputPortName = "OutA" },
                new ProjectGlobalVariableSourceBinding { Id = Guid.NewGuid(), VariableId = variableB, OperatorId = opB.Id, OutputPortId = portB, OperatorName = opB.Name, OutputPortName = "OutB" }
            ],
            TargetBindings =
            [
                new ProjectGlobalVariableTargetBinding { Id = Guid.NewGuid(), VariableId = variableA, OperatorId = opB.Id, ParameterId = paramB, OperatorName = opB.Name, ParameterName = "InB" },
                new ProjectGlobalVariableTargetBinding { Id = Guid.NewGuid(), VariableId = variableB, OperatorId = opA.Id, ParameterId = paramA, OperatorName = opA.Name, ParameterName = "InA" }
            ]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema, flow);

        diagnostics.Should().Contain(item => item.Code == "GV024" && item.Message.Contains("stats.a"));
    }

    [Fact]
    public void Validate_WhenVariableIncrementReferencesNonInt64Variable_ReturnsGv027()
    {
        var variableId = Guid.NewGuid();
        var increment = new Operator(Guid.NewGuid(), "Increment", OperatorType.VariableIncrement, 0, 0);
        increment.AddParameter(new Parameter(Guid.NewGuid(), "Scope", "Scope", "", "enum", "Project"));
        increment.AddParameter(new Parameter(Guid.NewGuid(), "VariableId", "VariableId", "", "string", variableId.ToString()));
        increment.AddParameter(new Parameter(Guid.NewGuid(), "VariableName", "VariableName", "", "string", "stats.count"));
        var flow = new OperatorFlow("increment-type");
        flow.AddOperator(increment);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables = [Variable("stats.count", ProjectGlobalVariableValueType.Double, 0.5, variableId)]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema, flow);

        diagnostics.Should().Contain(item => item.Code == "GV027");
    }

    [Fact]
    public void Validate_WhenVariableIdAndNameReferToDifferentVariables_ReturnsGv026()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var read = new Operator(Guid.NewGuid(), "Read", OperatorType.VariableRead, 0, 0);
        read.AddParameter(new Parameter(Guid.NewGuid(), "Scope", "Scope", "", "enum", "Project"));
        read.AddParameter(new Parameter(Guid.NewGuid(), "VariableId", "VariableId", "", "string", first.ToString()));
        read.AddParameter(new Parameter(Guid.NewGuid(), "VariableName", "VariableName", "", "string", "stats.second"));
        read.AddParameter(new Parameter(Guid.NewGuid(), "DataType", "DataType", "", "enum", "Int"));
        var flow = new OperatorFlow("mismatch");
        flow.AddOperator(read);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                Variable("stats.first", ProjectGlobalVariableValueType.Int64, 1, first),
                Variable("stats.second", ProjectGlobalVariableValueType.Int64, 2, second)
            ]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema, flow);

        diagnostics.Should().Contain(item => item.Code == "GV026");
    }

    [Fact]
    public void Validate_WhenBindingExpressionIsInvalid_ReturnsGv033()
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Count", PortDataType.Integer);
        var flow = new OperatorFlow("expression");
        flow.AddOperator(source);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables = [Variable("stats.count", ProjectGlobalVariableValueType.Int64, 0, variableId)],
            SourceBindings =
            [
                new ProjectGlobalVariableSourceBinding
                {
                    Id = Guid.NewGuid(),
                    VariableId = variableId,
                    OperatorId = source.Id,
                    OutputPortId = sourcePortId,
                    OperatorName = source.Name,
                    OutputPortName = "Count",
                    ConversionMode = ProjectVariableConversionMode.Floor,
                    Expression = "value ** 2"
                }
            ]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema, flow);

        diagnostics.Should().Contain(item => item.Code == "GV033");
    }

    [Fact]
    public void JsonRoundTrip_WhenInt64UsesFullRangeBounds_ShouldPreserveDecimalStrings()
    {
        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = "stats.total",
                    DisplayName = "stats.total",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(long.MaxValue),
                    MinBound = ProjectVariableNumericBound.FromInt64(long.MinValue),
                    MaxBound = ProjectVariableNumericBound.FromInt64(long.MaxValue)
                }
            ]
        };

        var json = JsonSerializer.Serialize(schema, ProjectVariableJson.Options);
        using var document = JsonDocument.Parse(json);
        var variableJson = document.RootElement.GetProperty("variables").EnumerateArray().Single();
        variableJson.GetProperty("initialValue").GetString().Should().Be(long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        variableJson.GetProperty("min").GetString().Should().Be(long.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        variableJson.GetProperty("max").GetString().Should().Be(long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var roundTripped = JsonSerializer.Deserialize<ProjectGlobalVariableSchema>(json, ProjectVariableJson.Options)!;
        var variable = roundTripped.Variables.Single();
        variable.InitialValue.GetInt64().Should().Be(long.MaxValue);
        variable.MinBound.Should().NotBeNull();
        variable.MaxBound.Should().NotBeNull();
        variable.MinBound!.Value.TryGetInt64(out var min).Should().BeTrue();
        variable.MaxBound!.Value.TryGetInt64(out var max).Should().BeTrue();
        min.Should().Be(long.MinValue);
        max.Should().Be(long.MaxValue);
        ProjectGlobalVariableSchemaValidator.Validate(roundTripped).Should().BeEmpty();
    }

    [Fact]
    public void Validate_WhenInt64BoundIsFractional_ReturnsDiagnostic()
    {
        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = "stats.total",
                    DisplayName = "stats.total",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(5L),
                    MinBound = "1.5"
                }
            ]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema);

        diagnostics.Should().Contain(item => item.Code == "GV018");
    }

    private static ProjectGlobalVariableDefinition Variable(
        string name,
        ProjectGlobalVariableValueType type,
        object initialValue,
        Guid? id = null)
    {
        return new ProjectGlobalVariableDefinition
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            DisplayName = name,
            ValueType = type,
            InitialValue = JsonSerializer.SerializeToElement(initialValue)
        };
    }
}
