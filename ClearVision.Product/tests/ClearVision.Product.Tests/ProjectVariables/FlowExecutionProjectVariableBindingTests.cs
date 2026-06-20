using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Tests.ProjectVariables;

public sealed class FlowExecutionProjectVariableBindingTests
{
    [Fact]
    public async Task ExecuteFlowAsync_WhenSourceWritesVariable_TargetReadsSameRunValue()
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var targetParameterId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Count", PortDataType.Integer);
        var target = new Operator(Guid.NewGuid(), "Target", OperatorType.ResultJudgment, 10, 0);
        target.AddParameter(new Parameter(targetParameterId, "ExpectedCount", "ExpectedCount", "", "int", 0));

        var flow = new OperatorFlow("implicit-variable-flow");
        flow.AddOperator(target);
        flow.AddOperator(source);

        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "last.detected_count",
                    DisplayName = "Detected Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(0)
                }
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
        using var session = new ProjectVariableSession(schema);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid());

        var sourceExecutor = Substitute.For<IOperatorExecutor>();
        sourceExecutor.OperatorType.Returns(OperatorType.Thresholding);
        sourceExecutor.ExecuteAsync(source, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Count"] = 3L
            })));
        sourceExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());

        var targetExecutor = Substitute.For<IOperatorExecutor>();
        targetExecutor.OperatorType.Returns(OperatorType.ResultJudgment);
        targetExecutor.ExecuteAsync(target, Arg.Is<Dictionary<string, object>>(inputs => Convert.ToInt64(inputs["ExpectedCount"]) == 3L), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>())));
        targetExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());

        var service = new FlowExecutionService(
            [sourceExecutor, targetExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            new ProjectVariableExecutionContextAccessor());

        var result = await service.ExecuteFlowAsync(flow, null, context);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        session.TryGetValue(variableId, out var current).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(current).Should().Be(3L);
        await targetExecutor.Received(1).ExecuteAsync(
            target,
            Arg.Is<Dictionary<string, object>>(inputs => Convert.ToInt64(inputs["ExpectedCount"]) == 3L),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenProjectVariableContextIsPreview_ShouldPropagateInCloneWithoutCommitting()
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var targetParameterId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Count", PortDataType.Integer);
        var target = new Operator(Guid.NewGuid(), "Target", OperatorType.ResultJudgment, 10, 0);
        target.AddParameter(new Parameter(targetParameterId, "ExpectedCount", "ExpectedCount", "", "int", 0));

        var flow = new OperatorFlow("preview-variable-flow");
        flow.AddOperator(target);
        flow.AddOperator(source);

        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "last.detected_count",
                    DisplayName = "Detected Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(0)
                }
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
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 2L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            isPreview: true);

        var sourceExecutor = Substitute.For<IOperatorExecutor>();
        sourceExecutor.OperatorType.Returns(OperatorType.Thresholding);
        sourceExecutor.ExecuteAsync(source, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Count"] = 7L
            })));
        sourceExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());

        var targetExecutor = Substitute.For<IOperatorExecutor>();
        targetExecutor.OperatorType.Returns(OperatorType.ResultJudgment);
        targetExecutor.ExecuteAsync(target, Arg.Is<Dictionary<string, object>>(inputs => Convert.ToInt64(inputs["ExpectedCount"]) == 7L), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>())));
        targetExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());

        var service = new FlowExecutionService(
            [sourceExecutor, targetExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            new ProjectVariableExecutionContextAccessor());

        var result = await service.ExecuteFlowAsync(flow, null, context);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        session.TryGetValue(variableId, out var current).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(current).Should().Be(2L);
        await targetExecutor.Received(1).ExecuteAsync(
            target,
            Arg.Is<Dictionary<string, object>>(inputs => Convert.ToInt64(inputs["ExpectedCount"]) == 7L),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenPreviewUsesVariableWriteAndIncrement_ShouldNotCommitToFormalSession()
    {
        var variableId = Guid.NewGuid();
        var accessor = new ProjectVariableExecutionContextAccessor();
        var write = new Operator(Guid.NewGuid(), "Write", OperatorType.VariableWrite, 0, 0);
        write.AddParameter(new Parameter(Guid.NewGuid(), "Scope", "Scope", "", "enum", "Project"));
        write.AddParameter(new Parameter(Guid.NewGuid(), "VariableId", "VariableId", "", "string", variableId.ToString()));
        write.AddParameter(new Parameter(Guid.NewGuid(), "VariableName", "VariableName", "", "string", "stats.count"));
        write.AddParameter(new Parameter(Guid.NewGuid(), "UseInputValue", "UseInputValue", "", "bool", false));
        write.AddParameter(new Parameter(Guid.NewGuid(), "StaticValue", "StaticValue", "", "string", "10"));
        write.AddParameter(new Parameter(Guid.NewGuid(), "DataType", "DataType", "", "enum", "Int"));
        var increment = new Operator(Guid.NewGuid(), "Increment", OperatorType.VariableIncrement, 20, 0);
        increment.AddParameter(new Parameter(Guid.NewGuid(), "Scope", "Scope", "", "enum", "Project"));
        increment.AddParameter(new Parameter(Guid.NewGuid(), "VariableId", "VariableId", "", "string", variableId.ToString()));
        increment.AddParameter(new Parameter(Guid.NewGuid(), "VariableName", "VariableName", "", "string", "stats.count"));
        increment.AddParameter(new Parameter(Guid.NewGuid(), "Delta", "Delta", "", "int", 5));

        var flow = new OperatorFlow("preview-variable-operators");
        flow.AddOperator(write);
        flow.AddOperator(increment);

        var schema = new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "stats.count",
                    DisplayName = "Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(1L)
                }
            ]
        };
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            isPreview: true);
        var service = new FlowExecutionService(
            [
                new ClearVision.Product.Infrastructure.Operators.VariableWriteOperator(
                    NullLogger<ClearVision.Product.Infrastructure.Operators.VariableWriteOperator>.Instance,
                    new VariableContext(),
                    accessor),
                new ClearVision.Product.Infrastructure.Operators.VariableIncrementOperator(
                    NullLogger<ClearVision.Product.Infrastructure.Operators.VariableIncrementOperator>.Instance,
                    new VariableContext(),
                    accessor)
            ],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            accessor);

        var result = await service.ExecuteFlowAsync(flow, null, context);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().ContainKey("NewValue");
        Convert.ToInt64(result.OutputData!["NewValue"]).Should().Be(15L);
        session.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(4L);
        formal.Version.Should().Be(1);
    }
}
