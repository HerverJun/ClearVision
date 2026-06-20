using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
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

    [Fact]
    public async Task ExecuteFlowDebugAsync_WhenProjectVariableReadUsesSameDebugSession_ShouldReadFreshFormalSnapshot()
    {
        var variableId = Guid.NewGuid();
        var accessor = new ProjectVariableExecutionContextAccessor();
        var read = CreateProjectVariableReadOperator(variableId);
        var flow = new OperatorFlow("debug-variable-read-cache");
        flow.AddOperator(read);
        var schema = CreateSingleInt64Schema(variableId, 1L);
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            isPreview: true);
        var readExecutor = new CountingExecutor(new VariableReadOperator(
            NullLogger<VariableReadOperator>.Instance,
            new VariableContext(),
            accessor));
        using var service = new FlowExecutionService(
            [readExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            accessor);
        var debugOptions = new DebugOptions
        {
            DebugSessionId = Guid.NewGuid(),
            EnableIntermediateCache = true
        };

        var first = await service.ExecuteFlowDebugAsync(flow, debugOptions, null, context);
        session.SetValue(variableId, 10L, ProjectVariableUpdatedBy.StudioManual);
        var second = await service.ExecuteFlowDebugAsync(flow, debugOptions, null, context);

        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        second.IsSuccess.Should().BeTrue(second.ErrorMessage);
        Convert.ToInt64(first.IntermediateResults[read.Id]["Value"]).Should().Be(4L);
        Convert.ToInt64(second.IntermediateResults[read.Id]["Value"]).Should().Be(10L);
        readExecutor.ExecuteCount.Should().Be(2, "Project-scope VariableRead must not reuse stale debug cache");
        service.GetDebugIntermediateResult(debugOptions.DebugSessionId, read.Id).Should().BeNull();
    }

    [Fact]
    public async Task ExecuteFlowDebugAsync_WhenProjectVariableIncrementFeedsTargetBinding_ShouldExecuteEveryPreviewClone()
    {
        var variableId = Guid.NewGuid();
        var targetParameterId = Guid.NewGuid();
        var accessor = new ProjectVariableExecutionContextAccessor();
        var increment = CreateProjectVariableIncrementOperator(variableId, 5);
        var target = new Operator(Guid.NewGuid(), "Target", OperatorType.ResultJudgment, 10, 0);
        target.AddParameter(new Parameter(targetParameterId, "ExpectedCount", "ExpectedCount", "", "int", 0));
        var flow = new OperatorFlow("debug-increment-target-binding");
        flow.AddOperator(increment);
        flow.AddOperator(target);
        var schema = CreateSingleInt64Schema(variableId, 1L);
        schema.TargetBindings.Add(new ProjectGlobalVariableTargetBinding
        {
            Id = Guid.NewGuid(),
            VariableId = variableId,
            OperatorId = target.Id,
            ParameterId = targetParameterId,
            OperatorName = target.Name,
            ParameterName = "ExpectedCount"
        });
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            isPreview: true);
        var incrementExecutor = new CountingExecutor(new VariableIncrementOperator(
            NullLogger<VariableIncrementOperator>.Instance,
            new VariableContext(),
            accessor));
        var targetExecutor = Substitute.For<IOperatorExecutor>();
        targetExecutor.OperatorType.Returns(OperatorType.ResultJudgment);
        targetExecutor.ExecuteAsync(
                target,
                Arg.Is<Dictionary<string, object>>(inputs => Convert.ToInt64(inputs["ExpectedCount"]) == 9L),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Seen"] = 9L
            })));
        targetExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        using var service = new FlowExecutionService(
            [incrementExecutor, targetExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            accessor);
        var debugOptions = new DebugOptions
        {
            DebugSessionId = Guid.NewGuid(),
            EnableIntermediateCache = true
        };

        var first = await service.ExecuteFlowDebugAsync(flow, debugOptions, null, context);
        var second = await service.ExecuteFlowDebugAsync(flow, debugOptions, null, context);

        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        second.IsSuccess.Should().BeTrue(second.ErrorMessage);
        Convert.ToInt64(first.IntermediateResults[target.Id]["Seen"]).Should().Be(9L);
        Convert.ToInt64(second.IntermediateResults[target.Id]["Seen"]).Should().Be(9L);
        incrementExecutor.ExecuteCount.Should().Be(2, "Project-scope VariableIncrement must update each preview clone");
        session.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(4L);
        formal.Version.Should().Be(1);
        service.GetDebugIntermediateResult(debugOptions.DebugSessionId, increment.Id).Should().BeNull();
    }

    [Fact]
    public async Task ExecuteFlowDebugAsync_WhenProjectVariableWriteFeedsRead_ShouldExecuteWriteEveryPreviewClone()
    {
        var variableId = Guid.NewGuid();
        var accessor = new ProjectVariableExecutionContextAccessor();
        var write = CreateProjectVariableWriteOperator(variableId, 12);
        var read = CreateProjectVariableReadOperator(variableId);
        var flow = new OperatorFlow("debug-write-read");
        flow.AddOperator(write);
        flow.AddOperator(read);
        var schema = CreateSingleInt64Schema(variableId, 1L);
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            isPreview: true);
        var writeExecutor = new CountingExecutor(new VariableWriteOperator(
            NullLogger<VariableWriteOperator>.Instance,
            new VariableContext(),
            accessor));
        var readExecutor = new CountingExecutor(new VariableReadOperator(
            NullLogger<VariableReadOperator>.Instance,
            new VariableContext(),
            accessor));
        using var service = new FlowExecutionService(
            [writeExecutor, readExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            accessor);
        var debugOptions = new DebugOptions
        {
            DebugSessionId = Guid.NewGuid(),
            EnableIntermediateCache = true
        };

        var first = await service.ExecuteFlowDebugAsync(flow, debugOptions, null, context);
        var second = await service.ExecuteFlowDebugAsync(flow, debugOptions, null, context);

        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        second.IsSuccess.Should().BeTrue(second.ErrorMessage);
        Convert.ToInt64(first.IntermediateResults[read.Id]["Value"]).Should().Be(12L);
        Convert.ToInt64(second.IntermediateResults[read.Id]["Value"]).Should().Be(12L);
        writeExecutor.ExecuteCount.Should().Be(2, "Project-scope VariableWrite must write each preview clone");
        readExecutor.ExecuteCount.Should().Be(2, "downstream Project-scope VariableRead must not replay stale output");
        session.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(4L);
        formal.Version.Should().Be(1);
        service.GetDebugIntermediateResult(debugOptions.DebugSessionId, write.Id).Should().BeNull();
        service.GetDebugIntermediateResult(debugOptions.DebugSessionId, read.Id).Should().BeNull();
    }

    [Fact]
    public async Task ExecuteFlowDebugAsync_WhenCachedSourceBindingFeedsVariableRead_ShouldReplaySourceBindingIntoCurrentClone()
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var accessor = new ProjectVariableExecutionContextAccessor();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Count", PortDataType.Integer);
        var read = CreateProjectVariableReadOperator(variableId);
        var flow = new OperatorFlow("debug-source-binding-cache");
        flow.AddOperator(source);
        flow.AddOperator(read);
        var schema = CreateSingleInt64Schema(variableId, 1L);
        schema.SourceBindings.Add(new ProjectGlobalVariableSourceBinding
        {
            Id = Guid.NewGuid(),
            VariableId = variableId,
            OperatorId = source.Id,
            OutputPortId = sourcePortId,
            OperatorName = source.Name,
            OutputPortName = "Count"
        });
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
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
        var readExecutor = new CountingExecutor(new VariableReadOperator(
            NullLogger<VariableReadOperator>.Instance,
            new VariableContext(),
            accessor));
        using var service = new FlowExecutionService(
            [sourceExecutor, readExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            accessor);
        var debugOptions = new DebugOptions
        {
            DebugSessionId = Guid.NewGuid(),
            EnableIntermediateCache = true
        };

        var first = await service.ExecuteFlowDebugAsync(flow, debugOptions, null, context);
        var second = await service.ExecuteFlowDebugAsync(flow, debugOptions, null, context);

        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        second.IsSuccess.Should().BeTrue(second.ErrorMessage);
        Convert.ToInt64(second.IntermediateResults[read.Id]["Value"]).Should().Be(7L);
        await sourceExecutor.Received(1).ExecuteAsync(
            source,
            Arg.Any<Dictionary<string, object>>(),
            Arg.Any<CancellationToken>());
        readExecutor.ExecuteCount.Should().Be(2);
        session.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(4L);
        service.GetDebugIntermediateResult(debugOptions.DebugSessionId, source.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteFlowDebugAsync_WhenTargetBindingValueChanges_ShouldReexecuteOrdinaryOperator()
    {
        var variableId = Guid.NewGuid();
        var targetParameterId = Guid.NewGuid();
        var target = new Operator(Guid.NewGuid(), "Target", OperatorType.ResultJudgment, 0, 0);
        target.AddParameter(new Parameter(targetParameterId, "ExpectedCount", "ExpectedCount", "", "int", 0));
        var flow = new OperatorFlow("debug-target-binding-fingerprint");
        flow.AddOperator(target);
        var schema = CreateSingleInt64Schema(variableId, 1L);
        schema.TargetBindings.Add(new ProjectGlobalVariableTargetBinding
        {
            Id = Guid.NewGuid(),
            VariableId = variableId,
            OperatorId = target.Id,
            ParameterId = targetParameterId,
            OperatorName = target.Name,
            ParameterName = "ExpectedCount"
        });
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            isPreview: true);
        var executeCount = 0;
        var targetExecutor = Substitute.For<IOperatorExecutor>();
        targetExecutor.OperatorType.Returns(OperatorType.ResultJudgment);
        targetExecutor.ExecuteAsync(target, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                executeCount++;
                var inputs = callInfo.ArgAt<Dictionary<string, object>>(1);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
                {
                    ["Seen"] = Convert.ToInt64(inputs["ExpectedCount"])
                }));
            });
        targetExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        using var service = new FlowExecutionService(
            [targetExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            new ProjectVariableExecutionContextAccessor());
        var debugOptions = new DebugOptions
        {
            DebugSessionId = Guid.NewGuid(),
            EnableIntermediateCache = true
        };

        var first = await service.ExecuteFlowDebugAsync(flow, debugOptions, null, context);
        session.SetValue(variableId, 10L, ProjectVariableUpdatedBy.StudioManual);
        var second = await service.ExecuteFlowDebugAsync(flow, debugOptions, null, context);

        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        second.IsSuccess.Should().BeTrue(second.ErrorMessage);
        Convert.ToInt64(first.IntermediateResults[target.Id]["Seen"]).Should().Be(4L);
        Convert.ToInt64(second.IntermediateResults[target.Id]["Seen"]).Should().Be(10L);
        executeCount.Should().Be(2, "target binding values are part of prepared inputs and should change the fingerprint");
    }

    private static ProjectGlobalVariableSchema CreateSingleInt64Schema(Guid variableId, long initialValue)
    {
        return new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "stats.count",
                    DisplayName = "Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(initialValue),
                    ManualWriteAllowed = true
                }
            ]
        };
    }

    private static Operator CreateProjectVariableReadOperator(Guid variableId)
    {
        var read = new Operator(Guid.NewGuid(), "Read", OperatorType.VariableRead, 0, 0);
        read.AddParameter(new Parameter(Guid.NewGuid(), "Scope", "Scope", "", "enum", "Project"));
        read.AddParameter(new Parameter(Guid.NewGuid(), "VariableId", "VariableId", "", "string", variableId.ToString()));
        read.AddParameter(new Parameter(Guid.NewGuid(), "VariableName", "VariableName", "", "string", "stats.count"));
        read.AddParameter(new Parameter(Guid.NewGuid(), "DefaultValue", "DefaultValue", "", "string", "0"));
        read.AddParameter(new Parameter(Guid.NewGuid(), "DataType", "DataType", "", "enum", "Int"));
        return read;
    }

    private static Operator CreateProjectVariableWriteOperator(Guid variableId, long value)
    {
        var write = new Operator(Guid.NewGuid(), "Write", OperatorType.VariableWrite, 0, 0);
        write.AddParameter(new Parameter(Guid.NewGuid(), "Scope", "Scope", "", "enum", "Project"));
        write.AddParameter(new Parameter(Guid.NewGuid(), "VariableId", "VariableId", "", "string", variableId.ToString()));
        write.AddParameter(new Parameter(Guid.NewGuid(), "VariableName", "VariableName", "", "string", "stats.count"));
        write.AddParameter(new Parameter(Guid.NewGuid(), "UseInputValue", "UseInputValue", "", "bool", false));
        write.AddParameter(new Parameter(Guid.NewGuid(), "StaticValue", "StaticValue", "", "string", value.ToString()));
        write.AddParameter(new Parameter(Guid.NewGuid(), "DataType", "DataType", "", "enum", "Int"));
        return write;
    }

    private static Operator CreateProjectVariableIncrementOperator(Guid variableId, long delta)
    {
        var increment = new Operator(Guid.NewGuid(), "Increment", OperatorType.VariableIncrement, 0, 0);
        increment.AddParameter(new Parameter(Guid.NewGuid(), "Scope", "Scope", "", "enum", "Project"));
        increment.AddParameter(new Parameter(Guid.NewGuid(), "VariableId", "VariableId", "", "string", variableId.ToString()));
        increment.AddParameter(new Parameter(Guid.NewGuid(), "VariableName", "VariableName", "", "string", "stats.count"));
        increment.AddParameter(new Parameter(Guid.NewGuid(), "Delta", "Delta", "", "int", delta));
        return increment;
    }

    private sealed class CountingExecutor : IOperatorExecutor
    {
        private readonly IOperatorExecutor _inner;

        public CountingExecutor(IOperatorExecutor inner)
        {
            _inner = inner;
        }

        public OperatorType OperatorType => _inner.OperatorType;

        public int ExecuteCount { get; private set; }

        public Task<OperatorExecutionOutput> ExecuteAsync(
            Operator @operator,
            Dictionary<string, object>? inputs = null,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return _inner.ExecuteAsync(@operator, inputs, cancellationToken);
        }

        public ValidationResult ValidateParameters(Operator @operator)
        {
            return _inner.ValidateParameters(@operator);
        }
    }
}
