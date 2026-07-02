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
    public void Validate_WhenVariableIdsDuplicateWithFlowBindings_ReturnsGv003WithoutThrowing()
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Count", PortDataType.Integer);
        var flow = new OperatorFlow("duplicate-variable-id");
        flow.AddOperator(source);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                Variable("stats.first", ProjectGlobalVariableValueType.Int64, 4, variableId),
                Variable("stats.second", ProjectGlobalVariableValueType.Int64, 5, variableId)
            ],
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
            ]
        };
        IReadOnlyList<ProjectGlobalVariableDiagnostic> diagnostics = [];

        var act = () => diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema, flow);

        act.Should().NotThrow();
        diagnostics.Should().Contain(item => item.Code == "GV003");
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
    public void BuildDependencyEdges_WhenBindingExpressionReadsVariable_AddsWriterToReaderEdge()
    {
        var sourceVariableId = Guid.NewGuid();
        var targetVariableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var targetParameterId = Guid.NewGuid();
        var writer = new Operator(Guid.NewGuid(), "Writer", OperatorType.Thresholding, 0, 0);
        writer.LoadOutputPort(sourcePortId, "Count", PortDataType.Integer);
        var reader = new Operator(Guid.NewGuid(), "Reader", OperatorType.ResultJudgment, 10, 0);
        reader.AddParameter(new Parameter(targetParameterId, "ExpectedCount", "ExpectedCount", "", "int", 0));
        var flow = new OperatorFlow("flow");
        flow.AddOperator(writer);
        flow.AddOperator(reader);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                Variable("stats.source", ProjectGlobalVariableValueType.Int64, 0, sourceVariableId),
                Variable("stats.target", ProjectGlobalVariableValueType.Int64, 0, targetVariableId)
            ],
            SourceBindings =
            [
                new ProjectGlobalVariableSourceBinding
                {
                    Id = Guid.NewGuid(),
                    VariableId = sourceVariableId,
                    OperatorId = writer.Id,
                    OutputPortId = sourcePortId,
                    OperatorName = writer.Name,
                    OutputPortName = "Count"
                }
            ],
            TargetBindings =
            [
                new ProjectGlobalVariableTargetBinding
                {
                    Id = Guid.NewGuid(),
                    VariableId = targetVariableId,
                    OperatorId = reader.Id,
                    ParameterId = targetParameterId,
                    OperatorName = reader.Name,
                    ParameterName = "ExpectedCount",
                    Expression = "value + stats.source"
                }
            ]
        };

        var edges = ProjectGlobalVariableFlowValidator.BuildDependencyEdges(flow, schema, [writer, reader]);

        edges.Should().Contain(edge =>
            edge.SourceOperatorId == writer.Id &&
            edge.TargetOperatorId == reader.Id &&
            edge.VariableName == "stats.source" &&
            edge.Kind == ProjectVariableFlowEdgeKind.GlobalVariable);
    }

    [Fact]
    public void BuildDependencyEdges_WhenOnlyVariableOperatorsExist_AddsWriteToReadEdgeAndEnablesCanonicalGraph()
    {
        var variableId = Guid.NewGuid();
        var read = CreateProjectVariableOperator(OperatorType.VariableRead, variableId, "Read");
        var write = CreateProjectVariableOperator(OperatorType.VariableWrite, variableId, "Write");
        var flow = new OperatorFlow("variable-operators-only");
        flow.AddOperator(read);
        flow.AddOperator(write);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables = [Variable("stats.count", ProjectGlobalVariableValueType.Int64, 0, variableId)]
        };

        var edges = ProjectGlobalVariableFlowValidator.BuildDependencyEdges(flow, schema, [read, write]);

        ProjectGlobalVariableFlowValidator.HasProjectVariableSemantics(flow, schema).Should().BeTrue();
        ProjectGlobalVariableFlowValidator.HasProjectVariableWriteCapability(flow, schema).Should().BeTrue();
        edges.Should().Contain(edge =>
            edge.SourceOperatorId == write.Id &&
            edge.TargetOperatorId == read.Id &&
            edge.VariableName == "stats.count" &&
            edge.Kind == ProjectVariableFlowEdgeKind.GlobalVariable);
    }

    [Fact]
    public void BuildDependencyEdges_WhenVariableIncrementFeedsTargetBinding_AddsIncrementToTargetEdge()
    {
        var variableId = Guid.NewGuid();
        var targetParameterId = Guid.NewGuid();
        var increment = CreateProjectVariableOperator(OperatorType.VariableIncrement, variableId, "Increment");
        var target = new Operator(Guid.NewGuid(), "Target", OperatorType.ResultJudgment, 10, 0);
        target.AddParameter(new Parameter(targetParameterId, "ExpectedCount", "ExpectedCount", "", "int", 0));
        var flow = new OperatorFlow("increment-target");
        flow.AddOperator(increment);
        flow.AddOperator(target);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables = [Variable("stats.count", ProjectGlobalVariableValueType.Int64, 0, variableId)],
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

        var edges = ProjectGlobalVariableFlowValidator.BuildDependencyEdges(flow, schema, [increment, target]);

        edges.Should().Contain(edge =>
            edge.SourceOperatorId == increment.Id &&
            edge.TargetOperatorId == target.Id &&
            edge.VariableName == "stats.count" &&
            edge.Kind == ProjectVariableFlowEdgeKind.GlobalVariable);
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
    public void Validate_WhenSourceBindingExpressionsCreateReadWriteCycle_ReturnsGv024()
    {
        var variableA = Guid.NewGuid();
        var variableB = Guid.NewGuid();
        var portA = Guid.NewGuid();
        var portB = Guid.NewGuid();
        var opA = new Operator(Guid.NewGuid(), "OperatorA", OperatorType.Thresholding, 0, 0);
        opA.LoadOutputPort(portA, "OutA", PortDataType.Integer);
        var opB = new Operator(Guid.NewGuid(), "OperatorB", OperatorType.Thresholding, 10, 0);
        opB.LoadOutputPort(portB, "OutB", PortDataType.Integer);
        var flow = new OperatorFlow("expression-cycle");
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
                new ProjectGlobalVariableSourceBinding
                {
                    Id = Guid.NewGuid(),
                    VariableId = variableA,
                    OperatorId = opA.Id,
                    OutputPortId = portA,
                    OperatorName = opA.Name,
                    OutputPortName = "OutA",
                    Expression = "value + stats.b"
                },
                new ProjectGlobalVariableSourceBinding
                {
                    Id = Guid.NewGuid(),
                    VariableId = variableB,
                    OperatorId = opB.Id,
                    OutputPortId = portB,
                    OperatorName = opB.Name,
                    OutputPortName = "OutB",
                    Expression = "value + stats.a"
                }
            ]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema, flow);

        diagnostics.Should().Contain(item =>
            item.Code == "GV024" &&
            item.Message.Contains("stats.a") &&
            item.Message.Contains("stats.b"));
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
    public void Validate_WhenVariableIdExistsButNameIsStale_ShouldUseIdAsAuthoritative()
    {
        var variableId = Guid.NewGuid();
        var read = new Operator(Guid.NewGuid(), "Read", OperatorType.VariableRead, 0, 0);
        read.AddParameter(new Parameter(Guid.NewGuid(), "Scope", "Scope", "", "enum", "Project"));
        read.AddParameter(new Parameter(Guid.NewGuid(), "VariableId", "VariableId", "", "string", variableId.ToString()));
        read.AddParameter(new Parameter(Guid.NewGuid(), "VariableName", "VariableName", "", "string", "stats.old"));
        read.AddParameter(new Parameter(Guid.NewGuid(), "DataType", "DataType", "", "enum", "Int"));
        var flow = new OperatorFlow("stale-name");
        flow.AddOperator(read);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables = [Variable("stats.current", ProjectGlobalVariableValueType.Int64, 1, variableId)]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema, flow);

        diagnostics.Select(item => item.Code).Should().NotContain("GV008").And.NotContain("GV026");
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

    [Theory]
    [InlineData("true || missing.value")]
    [InlineData("false && missing.value")]
    public void ExpressionEvaluator_WhenShortCircuitRhsReferencesUnknownVariable_ShouldReject(string expression)
    {
        var ok = ProjectVariableExpressionEvaluator.TryEvaluate(
            expression,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            out _,
            out var error);

        ok.Should().BeFalse();
        error.Should().Contain("Unknown variable");
    }

    [Fact]
    public void ExpressionEvaluator_WhenInt64ExceedsDoublePrecision_ShouldPreserveExactInteger()
    {
        var ok = ProjectVariableExpressionEvaluator.TryEvaluate(
            "9007199254740993 + 1",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            out var value,
            out var error);

        ok.Should().BeTrue(error);
        value.Should().Be(9007199254740994L);
    }

    [Fact]
    public void ExpressionEvaluator_WhenComparingLargeIntegers_ShouldPreserveOrdering()
    {
        var ok = ProjectVariableExpressionEvaluator.TryEvaluate(
            "9007199254740993 > 9007199254740992",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            out var value,
            out var error);

        ok.Should().BeTrue(error);
        value.Should().Be(true);
    }

    [Fact]
    public void ExpressionEvaluator_WhenDoubleFunctionWouldLoseLargeInt64Precision_ShouldReject()
    {
        var ok = ProjectVariableExpressionEvaluator.TryEvaluate(
            "sqrt(9007199254740993)",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            out _,
            out var error);

        ok.Should().BeFalse();
        error.Should().StartWith("GV038");
        error.Should().Contain("precision loss");
    }

    [Fact]
    public void ExpressionEvaluator_WhenDoubleFunctionUsesExactlyRepresentableInteger_ShouldEvaluate()
    {
        var ok = ProjectVariableExpressionEvaluator.TryEvaluate(
            "sqrt(144)",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            out var value,
            out var error);

        ok.Should().BeTrue(error);
        value.Should().Be(12d);
    }

    [Fact]
    public void ExpressionEvaluator_WhenPowUsesNonNegativeInt64Exponent_ShouldReturnInt64()
    {
        var ok = ProjectVariableExpressionEvaluator.TryEvaluate(
            "pow(2, 10)",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            out var value,
            out var error);

        ok.Should().BeTrue(error);
        value.Should().Be(1024L);
    }

    [Fact]
    public void ExpressionEvaluator_WhenInt64PowOverflows_ShouldReject()
    {
        var ok = ProjectVariableExpressionEvaluator.TryEvaluate(
            "pow(2, 63)",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            out _,
            out var error);

        ok.Should().BeFalse();
        error.Should().StartWith("GV037");
        error.Should().Contain("overflow");
    }

    [Fact]
    public void ExpressionEvaluator_WhenPowUsesNegativeExponent_ShouldUseDouble()
    {
        var ok = ProjectVariableExpressionEvaluator.TryEvaluate(
            "pow(2, -1)",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            out var value,
            out var error);

        ok.Should().BeTrue(error);
        value.Should().Be(0.5d);
    }

    [Fact]
    public void ExpressionEvaluator_WhenInt64ArithmeticOverflows_ShouldReject()
    {
        var ok = ProjectVariableExpressionEvaluator.TryEvaluate(
            $"{long.MaxValue} + 1",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            out _,
            out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ExpressionEvaluator_WhenDividingByZero_ShouldReject()
    {
        var ok = ProjectVariableExpressionEvaluator.TryEvaluate(
            "10 / 0",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            out _,
            out var error);

        ok.Should().BeFalse();
        error.Should().Contain("Division by zero");
    }

    [Theory]
    [InlineData("1 +", "GV034", "Expected expression")]
    [InlineData("missing.value + 1", "GV035", "Unknown variable")]
    [InlineData("10 / 0", "GV036", "Division by zero")]
    [InlineData("9223372036854775807 + 1", "GV037", "overflow")]
    [InlineData("sqrt(-1)", "GV038", "finite")]
    public void ExpressionEvaluator_WhenEvaluationFails_ShouldReturnStableErrorCode(
        string expression,
        string expectedCode,
        string expectedMessage)
    {
        var ok = ProjectVariableExpressionEvaluator.TryEvaluate(
            expression,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            out _,
            out var error);

        ok.Should().BeFalse();
        error.Should().StartWith(expectedCode);
        error.Should().Contain(expectedMessage);
    }

    [Fact]
    public void ExpressionEvaluator_TryCompile_ShouldParseWithoutEvaluatingRuntimeArithmetic()
    {
        var compiled = ProjectVariableExpressionEvaluator.TryCompile(
            "1 / 0",
            [],
            out var compileError);

        var evaluated = ProjectVariableExpressionEvaluator.TryEvaluate(
            "1 / 0",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            out _,
            out var evaluateError);

        compiled.Should().BeTrue(compileError);
        evaluated.Should().BeFalse();
        evaluateError.Should().Contain("Division by zero");
    }

    [Theory]
    [MemberData(nameof(ExpressionLimitCases))]
    public void ExpressionEvaluator_WhenExpressionExceedsSafetyLimits_ShouldReject(string expression, string expectedError)
    {
        var ok = ProjectVariableExpressionEvaluator.TryEvaluate(
            expression,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            out _,
            out var error);

        ok.Should().BeFalse();
        error.Should().StartWith("GV039");
        error.Should().Contain(expectedError);
    }

    [Fact]
    public void ValueTransform_WhenFlooringLargeFractionalNumericText_ShouldPreserveExactInt64()
    {
        var ok = ProjectVariableValueTransform.TryConvertToVariableValue(
            "9007199254740993.75",
            ProjectGlobalVariableValueType.Int64,
            ProjectVariableConversionMode.Floor,
            expression: null,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            out var converted,
            out var error);

        ok.Should().BeTrue(error);
        converted.GetInt64().Should().Be(9007199254740993L);
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

    [Fact]
    public void SourceBindingJsonRoundTrip_WhenResultPathFieldsAreMissing_ShouldRemainOldShapeAndValidateAsRoot()
    {
        var variableId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var portId = Guid.NewGuid();
        var json = $$"""
        {
          "schemaVersion": "1.0",
          "variables": [
            {
              "id": "{{variableId}}",
              "name": "stats.score",
              "displayName": "Score",
              "valueType": "Double",
              "initialValue": 0,
              "min": null,
              "max": null,
              "manualWriteAllowed": true,
              "includeInResultMetadata": false,
              "order": 0
            }
          ],
          "sourceBindings": [
            {
              "id": "{{Guid.NewGuid()}}",
              "variableId": "{{variableId}}",
              "operatorId": "{{operatorId}}",
              "outputPortId": "{{portId}}",
              "operatorName": "Source",
              "outputPortName": "Score",
              "conversionMode": "Exact",
              "expression": null
            }
          ],
          "targetBindings": []
        }
        """;

        var schema = JsonSerializer.Deserialize<ProjectGlobalVariableSchema>(json, ProjectVariableJson.Options)!;
        var serialized = JsonSerializer.Serialize(schema, ProjectVariableJson.Options);

        schema.SourceBindings.Single().ResultPathVersion.Should().BeNull();
        schema.SourceBindings.Single().ResultPath.Should().BeNull();
        serialized.Should().NotContain("resultPath", because: "missing optional ResultPath fields should not rewrite old project JSON");
        ProjectGlobalVariableSchemaValidator.Validate(schema).Should().NotContain(item => item.Code.StartsWith("RP1", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenSourceBindingUsesCanonicalResultPathAndExpression_ReturnsNoResultPathDiagnostics()
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Payload", PortDataType.Any);
        var flow = new OperatorFlow("resultpath-source-binding");
        flow.AddOperator(source);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables = [Variable("stats.score", ProjectGlobalVariableValueType.Int64, 0, variableId)],
            SourceBindings =
            [
                new ProjectGlobalVariableSourceBinding
                {
                    Id = Guid.NewGuid(),
                    VariableId = variableId,
                    OperatorId = source.Id,
                    OutputPortId = sourcePortId,
                    OperatorName = source.Name,
                    OutputPortName = "Payload",
                    ResultPathVersion = 1,
                    ResultPath = "$[\"Score\"]",
                    Expression = "value + 1"
                }
            ]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema, flow);

        diagnostics.Should().NotContain(item => item.Code.StartsWith("RP1", StringComparison.Ordinal));
        schema.SourceBindings.Single().Expression.Should().Be("value + 1");
        schema.SourceBindings.Single().ResultPath.Should().Be("$[\"Score\"]");
    }

    [Theory]
    [InlineData(null, "$[\"Score\"]", "RP101")]
    [InlineData(1, null, "RP101")]
    [InlineData(2, "$[\"Score\"]", "RP100")]
    [InlineData(1, "", "RP101")]
    [InlineData(1, "$.Score", "RP104")]
    [InlineData(1, "$[\"\\u0053core\"]", "RP107")]
    public void Validate_WhenSourceBindingResultPathPairIsInvalid_ReturnsVersionedDiagnostic(
        int? version,
        string? path,
        string expectedCode)
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Payload", PortDataType.Any);
        var flow = new OperatorFlow("invalid-resultpath-source-binding");
        flow.AddOperator(source);
        var schema = new ProjectGlobalVariableSchema
        {
            Variables = [Variable("stats.score", ProjectGlobalVariableValueType.Int64, 0, variableId)],
            SourceBindings =
            [
                new ProjectGlobalVariableSourceBinding
                {
                    Id = Guid.NewGuid(),
                    VariableId = variableId,
                    OperatorId = source.Id,
                    OutputPortId = sourcePortId,
                    OperatorName = source.Name,
                    OutputPortName = "Payload",
                    ResultPathVersion = version,
                    ResultPath = path
                }
            ]
        };

        var diagnostics = ProjectGlobalVariableSchemaValidator.Validate(schema, flow);

        diagnostics.Should().Contain(item => item.Code == expectedCode);
    }

    public static IEnumerable<object[]> ExpressionLimitCases()
    {
        yield return
        [
            new string('1', ProjectVariableExpressionEvaluator.MaxExpressionLength + 1),
            $"maximum length {ProjectVariableExpressionEvaluator.MaxExpressionLength}"
        ];
        yield return
        [
            string.Join("+", Enumerable.Repeat("1", ProjectVariableExpressionEvaluator.MaxTokenCount + 1)),
            $"token count exceeds {ProjectVariableExpressionEvaluator.MaxTokenCount}"
        ];
        yield return
        [
            new string('(', ProjectVariableExpressionEvaluator.MaxAstDepth + 1) +
                "1" +
                new string(')', ProjectVariableExpressionEvaluator.MaxAstDepth + 1),
            $"AST depth exceeds {ProjectVariableExpressionEvaluator.MaxAstDepth}"
        ];
        yield return
        [
            "max(" + string.Join(", ", Enumerable.Range(1, ProjectVariableExpressionEvaluator.MaxFunctionArgumentCount + 1)) + ")",
            $"function argument count exceeds {ProjectVariableExpressionEvaluator.MaxFunctionArgumentCount}"
        ];
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

    private static Operator CreateProjectVariableOperator(OperatorType type, Guid variableId, string name)
    {
        var op = new Operator(Guid.NewGuid(), name, type, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "Scope", "Scope", "", "enum", "Project"));
        op.AddParameter(new Parameter(Guid.NewGuid(), "VariableId", "VariableId", "", "string", variableId.ToString()));
        op.AddParameter(new Parameter(Guid.NewGuid(), "VariableName", "VariableName", "", "string", "stats.count"));
        op.AddParameter(new Parameter(Guid.NewGuid(), "DataType", "DataType", "", "enum", "Int"));
        return op;
    }
}
