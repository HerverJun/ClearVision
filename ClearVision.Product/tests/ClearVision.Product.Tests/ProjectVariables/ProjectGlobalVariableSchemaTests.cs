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
