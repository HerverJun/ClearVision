using System.Diagnostics;
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
            Guid.NewGuid(),
            commitHandler: CreateInMemoryCommitHandler(session));

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
    public async Task ExecuteFlowAsync_WhenFormalWriteMissingCommitHandler_ShouldRejectBeforeFirstOperator()
    {
        var variableId = Guid.NewGuid();
        var accessor = new ProjectVariableExecutionContextAccessor();
        var write = CreateProjectVariableWriteOperator(variableId, 12);
        var flow = new OperatorFlow("formal-write-without-commit-handler");
        flow.AddOperator(write);
        var schema = CreateSingleInt64Schema(variableId, 1L);
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid());
        var writeExecutor = new CountingExecutor(new VariableWriteOperator(
            NullLogger<VariableWriteOperator>.Instance,
            new VariableContext(),
            accessor));
        using var service = new FlowExecutionService(
            [writeExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            accessor);

        var result = await service.ExecuteFlowAsync(flow, null, context);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("GV040");
        writeExecutor.ExecuteCount.Should().Be(0);
        session.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(4L);
        formal.Version.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenVariableIncrementSucceedsButLaterOperatorFails_ShouldDiscardWorkingCopy()
    {
        var variableId = Guid.NewGuid();
        var accessor = new ProjectVariableExecutionContextAccessor();
        var increment = CreateProjectVariableIncrementOperator(variableId, 5);
        var failing = new Operator(Guid.NewGuid(), "FailingPlc", OperatorType.ResultJudgment, 10, 0);
        var flow = new OperatorFlow("increment-rollback");
        flow.AddOperator(increment);
        flow.AddOperator(failing);
        var schema = CreateSingleInt64Schema(variableId, 1L);
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            commitHandler: CreateInMemoryCommitHandler(session));
        var failingExecutor = Substitute.For<IOperatorExecutor>();
        failingExecutor.OperatorType.Returns(OperatorType.ResultJudgment);
        failingExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        failingExecutor.ExecuteAsync(failing, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Failure("PLC write failed")));
        using var service = new FlowExecutionService(
            [
                new VariableIncrementOperator(
                    NullLogger<VariableIncrementOperator>.Instance,
                    new VariableContext(),
                    accessor),
                failingExecutor
            ],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            accessor);

        var result = await service.ExecuteFlowAsync(flow, null, context);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("PLC write failed");
        session.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(4L);
        formal.Version.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenRunIsCanceledAfterProjectVariableWrite_ShouldNotCommitWorkingCopy()
    {
        var variableId = Guid.NewGuid();
        var accessor = new ProjectVariableExecutionContextAccessor();
        var write = CreateProjectVariableWriteOperator(variableId, 12);
        var canceling = new Operator(Guid.NewGuid(), "CancelingPlc", OperatorType.ResultJudgment, 10, 0);
        var flow = new OperatorFlow("write-cancel-rollback");
        flow.AddOperator(write);
        flow.AddOperator(canceling);
        var schema = CreateSingleInt64Schema(variableId, 1L);
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var commitCalls = 0;
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            commitHandler: (_, _) =>
            {
                commitCalls++;
                return ProjectVariableCommitResult.Success();
            });
        using var cancellation = new CancellationTokenSource();
        var cancelingExecutor = Substitute.For<IOperatorExecutor>();
        cancelingExecutor.OperatorType.Returns(OperatorType.ResultJudgment);
        cancelingExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        cancelingExecutor.ExecuteAsync(canceling, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>()));
            });
        using var service = new FlowExecutionService(
            [
                new VariableWriteOperator(
                    NullLogger<VariableWriteOperator>.Instance,
                    new VariableContext(),
                    accessor),
                cancelingExecutor
            ],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            accessor);

        var result = await service.ExecuteFlowAsync(flow, null, context, cancellationToken: cancellation.Token);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Flow was canceled.");
        commitCalls.Should().Be(0);
        session.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(4L);
        formal.Version.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenRunShortCircuitsAfterProjectVariableWrite_ShouldNotCommitWorkingCopy()
    {
        var variableId = Guid.NewGuid();
        var accessor = new ProjectVariableExecutionContextAccessor();
        var write = CreateProjectVariableWriteOperator(variableId, 12);
        var shortCircuit = new Operator(Guid.NewGuid(), "ShortCircuitTrigger", OperatorType.FrameChangeTrigger, 10, 0);
        var flow = new OperatorFlow("write-short-circuit-rollback");
        flow.AddOperator(write);
        flow.AddOperator(shortCircuit);
        var schema = CreateSingleInt64Schema(variableId, 1L);
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var commitCalls = 0;
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            commitHandler: (_, _) =>
            {
                commitCalls++;
                return ProjectVariableCommitResult.Success();
            });
        var shortCircuitExecutor = Substitute.For<IOperatorExecutor>();
        shortCircuitExecutor.OperatorType.Returns(OperatorType.FrameChangeTrigger);
        shortCircuitExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        shortCircuitExecutor.ExecuteAsync(shortCircuit, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.ShortCircuit(new Dictionary<string, object>())));
        using var service = new FlowExecutionService(
            [
                new VariableWriteOperator(
                    NullLogger<VariableWriteOperator>.Instance,
                    new VariableContext(),
                    accessor),
                shortCircuitExecutor
            ],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            accessor);

        var result = await service.ExecuteFlowAsync(flow, null, context);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.WasShortCircuited.Should().BeTrue();
        commitCalls.Should().Be(0);
        session.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(4L);
        formal.Version.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenAuthorityChangesBeforeCommit_ShouldRejectStaleTransaction()
    {
        var variableId = Guid.NewGuid();
        var accessor = new ProjectVariableExecutionContextAccessor();
        var write = CreateProjectVariableWriteOperator(variableId, 12);
        var concurrent = new Operator(Guid.NewGuid(), "ConcurrentManualWrite", OperatorType.ResultJudgment, 10, 0);
        var flow = new OperatorFlow("write-conflict");
        flow.AddOperator(write);
        flow.AddOperator(concurrent);
        var schema = CreateSingleInt64Schema(variableId, 1L);
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            commitHandler: CreateInMemoryCommitHandler(session));
        var concurrentExecutor = Substitute.For<IOperatorExecutor>();
        concurrentExecutor.OperatorType.Returns(OperatorType.ResultJudgment);
        concurrentExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        concurrentExecutor.ExecuteAsync(concurrent, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                session.SetValue(variableId, 20L, ProjectVariableUpdatedBy.StudioManual);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>()));
            });
        using var service = new FlowExecutionService(
            [
                new VariableWriteOperator(
                    NullLogger<VariableWriteOperator>.Instance,
                    new VariableContext(),
                    accessor),
                concurrentExecutor
            ],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            accessor);

        var result = await service.ExecuteFlowAsync(flow, null, context);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("GV025");
        session.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(20L);
        formal.Version.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenCommitHandlerFails_ShouldFailRunAndKeepFormalSessionUnchanged()
    {
        var variableId = Guid.NewGuid();
        var accessor = new ProjectVariableExecutionContextAccessor();
        var write = CreateProjectVariableWriteOperator(variableId, 12);
        var flow = new OperatorFlow("write-persist-failure");
        flow.AddOperator(write);
        var schema = CreateSingleInt64Schema(variableId, 1L);
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            commitHandler: (_, _) => ProjectVariableCommitResult.Failure("GV030: simulated state-store failure"));
        using var service = new FlowExecutionService(
            [
                new VariableWriteOperator(
                    NullLogger<VariableWriteOperator>.Instance,
                    new VariableContext(),
                    accessor)
            ],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            accessor);

        var result = await service.ExecuteFlowAsync(flow, null, context);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("GV030");
        session.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(4L);
        formal.Version.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenSourceBindingUsesFloorConversion_ShouldStoreInt64()
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Score", PortDataType.Float);
        var flow = new OperatorFlow("source-floor-conversion");
        flow.AddOperator(source);
        var schema = CreateSingleInt64Schema(variableId, 0L);
        schema.SourceBindings.Add(new ProjectGlobalVariableSourceBinding
        {
            Id = Guid.NewGuid(),
            VariableId = variableId,
            OperatorId = source.Id,
            OutputPortId = sourcePortId,
            OperatorName = source.Name,
            OutputPortName = "Score",
            ConversionMode = ProjectVariableConversionMode.Floor
        });
        using var session = new ProjectVariableSession(schema);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            commitHandler: CreateInMemoryCommitHandler(session));
        var sourceExecutor = Substitute.For<IOperatorExecutor>();
        sourceExecutor.OperatorType.Returns(OperatorType.Thresholding);
        sourceExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        sourceExecutor.ExecuteAsync(source, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Score"] = 7.9
            })));
        using var service = new FlowExecutionService(
            [sourceExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            new ProjectVariableExecutionContextAccessor());

        var result = await service.ExecuteFlowAsync(flow, null, context);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        session.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(7L);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteFlowAsync_WhenSourceBindingUsesResultPath_ShouldResolveThenApplyExpression(bool enableParallel)
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var targetParameterId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Payload", PortDataType.Any);
        var target = new Operator(Guid.NewGuid(), "Target", OperatorType.ResultJudgment, 10, 0);
        target.AddParameter(new Parameter(targetParameterId, "ExpectedCount", "ExpectedCount", "", "int", 0));
        var flow = new OperatorFlow("resultpath-expression-formal");
        flow.AddOperator(target);
        flow.AddOperator(source);
        var schema = CreateSingleInt64Schema(variableId, 0L);
        schema.SourceBindings.Add(new ProjectGlobalVariableSourceBinding
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
        });
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
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            commitHandler: CreateInMemoryCommitHandler(session));
        var sourceExecutor = CreatePayloadSourceExecutor(source, new Dictionary<string, object?>
        {
            ["Score"] = 4L
        });
        var targetExecutor = Substitute.For<IOperatorExecutor>();
        targetExecutor.OperatorType.Returns(OperatorType.ResultJudgment);
        targetExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        targetExecutor.ExecuteAsync(
                target,
                Arg.Is<Dictionary<string, object>>(inputs => Convert.ToInt64(inputs["ExpectedCount"]) == 5L),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Seen"] = 5L
            })));
        using var service = new FlowExecutionService(
            [sourceExecutor, targetExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            new ProjectVariableExecutionContextAccessor());

        var result = await service.ExecuteFlowAsync(flow, null, context, enableParallel);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().ContainKey("Seen");
        Convert.ToInt64(result.OutputData!["Seen"]).Should().Be(5L);
        session.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(5L);
    }

    [Fact]
    public async Task ExecuteFlowDebugAsync_WhenPreviewSourceBindingUsesResultPath_ShouldMatchFormalValueWithoutCommitting()
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var targetParameterId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Payload", PortDataType.Any);
        var target = new Operator(Guid.NewGuid(), "Target", OperatorType.ResultJudgment, 10, 0);
        target.AddParameter(new Parameter(targetParameterId, "ExpectedCount", "ExpectedCount", "", "int", 0));
        var flow = new OperatorFlow("resultpath-expression-preview");
        flow.AddOperator(source);
        flow.AddOperator(target);
        var schema = CreateSingleInt64Schema(variableId, 0L);
        schema.SourceBindings.Add(new ProjectGlobalVariableSourceBinding
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
        });
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
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            isPreview: true);
        var sourceExecutor = CreatePayloadSourceExecutor(source, new Dictionary<string, object?>
        {
            ["Score"] = 4L
        });
        var targetExecutor = Substitute.For<IOperatorExecutor>();
        targetExecutor.OperatorType.Returns(OperatorType.ResultJudgment);
        targetExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        targetExecutor.ExecuteAsync(
                target,
                Arg.Is<Dictionary<string, object>>(inputs => Convert.ToInt64(inputs["ExpectedCount"]) == 5L),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Seen"] = 5L
            })));
        using var service = new FlowExecutionService(
            [sourceExecutor, targetExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            new ProjectVariableExecutionContextAccessor());

        var result = await service.ExecuteFlowDebugAsync(
            flow,
            new DebugOptions { DebugSessionId = Guid.NewGuid() },
            null,
            context);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToInt64(result.IntermediateResults[target.Id]["Seen"]).Should().Be(5L);
        session.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(0L);
        formal.Version.Should().Be(0);
    }

    [Theory]
    [InlineData("$[0]", "RP120")]
    [InlineData("$[\"Missing\"]", "RP111")]
    public async Task ExecuteFlowAsync_WhenSourceBindingResultPathIsInvalidOrMissing_ShouldFailWithoutSessionWrite(
        string resultPath,
        string diagnosticCode)
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Payload", PortDataType.Any);
        var flow = new OperatorFlow("resultpath-fail-closed");
        flow.AddOperator(source);
        var schema = CreateSingleInt64Schema(variableId, 0L);
        schema.SourceBindings.Add(new ProjectGlobalVariableSourceBinding
        {
            Id = Guid.NewGuid(),
            VariableId = variableId,
            OperatorId = source.Id,
            OutputPortId = sourcePortId,
            OperatorName = source.Name,
            OutputPortName = "Payload",
            ResultPathVersion = 1,
            ResultPath = resultPath
        });
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            commitHandler: CreateInMemoryCommitHandler(session));
        var sourceExecutor = CreatePayloadSourceExecutor(source, new Dictionary<string, object?>
        {
            ["Score"] = 5L
        });
        using var service = new FlowExecutionService(
            [sourceExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            new ProjectVariableExecutionContextAccessor());

        var result = await service.ExecuteFlowAsync(flow, null, context);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain(diagnosticCode);
        session.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(4L);
        snapshot.Version.Should().Be(1);
    }

    [Theory]
    [InlineData(null, "$[\"Score\"]", "RP101")]
    [InlineData(1, null, "RP101")]
    [InlineData(2, "$[\"Score\"]", "RP100")]
    [InlineData(1, "$[\"\\u0053core\"]", "RP107")]
    public async Task ExecuteFlowAsync_WhenSourceBindingResultPathPairIsInvalid_ShouldFailWithoutSessionWrite(
        int? resultPathVersion,
        string? resultPath,
        string diagnosticCode)
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Payload", PortDataType.Any);
        var flow = new OperatorFlow("resultpath-runtime-pair-fail-closed");
        flow.AddOperator(source);
        var schema = CreateSingleInt64Schema(variableId, 0L);
        schema.SourceBindings.Add(new ProjectGlobalVariableSourceBinding
        {
            Id = Guid.NewGuid(),
            VariableId = variableId,
            OperatorId = source.Id,
            OutputPortId = sourcePortId,
            OperatorName = source.Name,
            OutputPortName = "Payload",
            ResultPathVersion = resultPathVersion,
            ResultPath = resultPath,
            Expression = "value + 1"
        });
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            commitHandler: CreateInMemoryCommitHandler(session));
        var sourceExecutor = CreatePayloadSourceExecutor(source, new Dictionary<string, object?>
        {
            ["Score"] = 5L
        });
        using var service = new FlowExecutionService(
            [sourceExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            new ProjectVariableExecutionContextAccessor());

        var result = await service.ExecuteFlowAsync(flow, null, context);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain(diagnosticCode);
        session.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(4L);
        snapshot.Version.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenNestedResultPathRequiresUnsupportedStructuredObjectTraversal_ShouldFailWithoutSessionWrite()
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Detection", PortDataType.Any);
        var flow = new OperatorFlow("resultpath-structured-object-fail-closed");
        flow.AddOperator(source);
        var schema = CreateSingleInt64Schema(variableId, 0L);
        schema.SourceBindings.Add(new ProjectGlobalVariableSourceBinding
        {
            Id = Guid.NewGuid(),
            VariableId = variableId,
            OperatorId = source.Id,
            OutputPortId = sourcePortId,
            OperatorName = source.Name,
            OutputPortName = "Detection",
            ResultPathVersion = 1,
            ResultPath = "$[\"Confidence\"]"
        });
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            commitHandler: CreateInMemoryCommitHandler(session));
        var sourceExecutor = Substitute.For<IOperatorExecutor>();
        sourceExecutor.OperatorType.Returns(OperatorType.Thresholding);
        sourceExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        sourceExecutor.ExecuteAsync(source, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Detection"] = new ClearVision.Product.Core.ValueObjects.DetectionResult("defect", 0.75f, 1, 2, 3, 4)
            })));
        using var service = new FlowExecutionService(
            [sourceExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            new ProjectVariableExecutionContextAccessor());

        var result = await service.ExecuteFlowAsync(flow, null, context);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("RP110");
        session.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(4L);
        snapshot.Version.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenSourceBindingResultPathResolvesResourceLikeValue_ShouldFailWithoutSessionWrite()
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Payload", PortDataType.Any);
        var flow = new OperatorFlow("resultpath-resource-fail-closed");
        flow.AddOperator(source);
        var schema = CreateSingleInt64Schema(variableId, 0L);
        schema.SourceBindings.Add(new ProjectGlobalVariableSourceBinding
        {
            Id = Guid.NewGuid(),
            VariableId = variableId,
            OperatorId = source.Id,
            OutputPortId = sourcePortId,
            OperatorName = source.Name,
            OutputPortName = "Payload",
            ResultPathVersion = 1,
            ResultPath = "$"
        });
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            commitHandler: CreateInMemoryCommitHandler(session));
        var sourceExecutor = Substitute.For<IOperatorExecutor>();
        sourceExecutor.OperatorType.Returns(OperatorType.Thresholding);
        sourceExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        sourceExecutor.ExecuteAsync(source, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Payload"] = new Image()
            })));
        using var service = new FlowExecutionService(
            [sourceExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            new ProjectVariableExecutionContextAccessor());

        var result = await service.ExecuteFlowAsync(flow, null, context);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("RP119");
        session.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(4L);
        snapshot.Version.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenTargetBindingUsesRoundExpression_ShouldApplyIntegerParameter()
    {
        var variableId = Guid.NewGuid();
        var targetParameterId = Guid.NewGuid();
        var target = new Operator(Guid.NewGuid(), "Target", OperatorType.ResultJudgment, 0, 0);
        target.AddParameter(new Parameter(targetParameterId, "ExpectedCount", "ExpectedCount", "", "int", 0));
        var flow = new OperatorFlow("target-round-expression");
        flow.AddOperator(target);
        var schema = CreateSingleDoubleSchema(variableId, 2.25);
        schema.TargetBindings.Add(new ProjectGlobalVariableTargetBinding
        {
            Id = Guid.NewGuid(),
            VariableId = variableId,
            OperatorId = target.Id,
            ParameterId = targetParameterId,
            OperatorName = target.Name,
            ParameterName = "ExpectedCount",
            ConversionMode = ProjectVariableConversionMode.Round,
            Expression = "value * 2 + 0.25"
        });
        using var session = new ProjectVariableSession(schema);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid());
        var targetExecutor = Substitute.For<IOperatorExecutor>();
        targetExecutor.OperatorType.Returns(OperatorType.ResultJudgment);
        targetExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        targetExecutor.ExecuteAsync(
                target,
                Arg.Is<Dictionary<string, object>>(inputs => Convert.ToInt64(inputs["ExpectedCount"]) == 5L),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Seen"] = 5L
            })));
        using var service = new FlowExecutionService(
            [targetExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            new ProjectVariableExecutionContextAccessor());

        var result = await service.ExecuteFlowAsync(flow, null, context);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().ContainKey("Seen");
        Convert.ToInt64(result.OutputData!["Seen"]).Should().Be(5L);
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenParallelModeUsesProjectVariables_ShouldRunIndependentLayerConcurrentlyAndRespectImplicitEdge()
    {
        var variableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var targetParameterId = Guid.NewGuid();
        var target = new Operator(Guid.NewGuid(), "Target", OperatorType.ResultJudgment, 20, 0);
        target.AddParameter(new Parameter(targetParameterId, "ExpectedCount", "ExpectedCount", "", "int", 0));
        var source = new Operator(Guid.NewGuid(), "Source", OperatorType.Thresholding, 0, 0);
        source.LoadOutputPort(sourcePortId, "Count", PortDataType.Integer);
        var independent = new Operator(Guid.NewGuid(), "Independent", OperatorType.BlobAnalysis, 10, 0);
        var flow = new OperatorFlow("parallel-project-variable-flow");
        flow.AddOperator(target);
        flow.AddOperator(source);
        flow.AddOperator(independent);
        var schema = CreateSingleInt64Schema(variableId, 0L);
        schema.SourceBindings.Add(new ProjectGlobalVariableSourceBinding
        {
            Id = Guid.NewGuid(),
            VariableId = variableId,
            OperatorId = source.Id,
            OutputPortId = sourcePortId,
            OperatorName = source.Name,
            OutputPortName = "Count"
        });
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
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            commitHandler: CreateInMemoryCommitHandler(session));
        var clock = Stopwatch.StartNew();
        long sourceStartedAt = -1;
        long sourceCompletedAt = -1;
        long independentStartedAt = -1;
        long targetStartedAt = -1;
        var sourceExecutor = Substitute.For<IOperatorExecutor>();
        sourceExecutor.OperatorType.Returns(OperatorType.Thresholding);
        sourceExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        sourceExecutor.ExecuteAsync(source, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                sourceStartedAt = clock.ElapsedMilliseconds;
                await Task.Delay(150);
                sourceCompletedAt = clock.ElapsedMilliseconds;
                return OperatorExecutionOutput.Success(new Dictionary<string, object>
                {
                    ["Count"] = 11L
                });
            });
        var independentExecutor = Substitute.For<IOperatorExecutor>();
        independentExecutor.OperatorType.Returns(OperatorType.BlobAnalysis);
        independentExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        independentExecutor.ExecuteAsync(independent, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                independentStartedAt = clock.ElapsedMilliseconds;
                await Task.Delay(10);
                return OperatorExecutionOutput.Success(new Dictionary<string, object>());
            });
        var targetExecutor = Substitute.For<IOperatorExecutor>();
        targetExecutor.OperatorType.Returns(OperatorType.ResultJudgment);
        targetExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        targetExecutor.ExecuteAsync(
                target,
                Arg.Is<Dictionary<string, object>>(inputs => Convert.ToInt64(inputs["ExpectedCount"]) == 11L),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                targetStartedAt = clock.ElapsedMilliseconds;
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
                {
                    ["Seen"] = 11L
                }));
            });
        using var service = new FlowExecutionService(
            [sourceExecutor, independentExecutor, targetExecutor],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            new ProjectVariableExecutionContextAccessor());

        var result = await service.ExecuteFlowAsync(flow, null, context, enableParallel: true);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().ContainKey("Seen");
        Convert.ToInt64(result.OutputData!["Seen"]).Should().Be(11L);
        await sourceExecutor.Received(1).ExecuteAsync(source, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>());
        await independentExecutor.Received(1).ExecuteAsync(independent, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>());
        await targetExecutor.Received(1).ExecuteAsync(target, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>());
        independentStartedAt.Should().BeGreaterThanOrEqualTo(0);
        sourceCompletedAt.Should().BeGreaterThan(sourceStartedAt);
        independentStartedAt.Should().BeLessThan(sourceCompletedAt);
        targetStartedAt.Should().BeGreaterThanOrEqualTo(sourceCompletedAt);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteFlowAsync_WhenVariableWriteAppearsAfterRead_ShouldUseDependencyGraphOrder(bool enableParallel)
    {
        var variableId = Guid.NewGuid();
        var accessor = new ProjectVariableExecutionContextAccessor();
        var read = CreateProjectVariableReadOperator(variableId);
        var write = CreateProjectVariableWriteOperator(variableId, 42);
        var flow = new OperatorFlow("write-read-graph-order");
        flow.AddOperator(read);
        flow.AddOperator(write);
        var schema = CreateSingleInt64Schema(variableId, 1L);
        using var session = new ProjectVariableSession(schema);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid(),
            commitHandler: CreateInMemoryCommitHandler(session));
        using var service = new FlowExecutionService(
            [
                new VariableReadOperator(
                    NullLogger<VariableReadOperator>.Instance,
                    new VariableContext(),
                    accessor),
                new VariableWriteOperator(
                    NullLogger<VariableWriteOperator>.Instance,
                    new VariableContext(),
                    accessor)
            ],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            accessor);

        var result = await service.ExecuteFlowAsync(flow, null, context, enableParallel);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().ContainKey("Value");
        Convert.ToInt64(result.OutputData!["Value"]).Should().Be(42L);
        session.TryGetSnapshot(variableId, out var snapshot).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(snapshot.Value).Should().Be(42L);
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

    private static ProjectVariableCommitHandler CreateInMemoryCommitHandler(IProjectVariableSession session)
    {
        return (workingSession, expectedVersions) =>
            session.TryCommitFrom(workingSession, expectedVersions, out var error)
                ? ProjectVariableCommitResult.Success()
                : ProjectVariableCommitResult.Failure(error);
    }

    private static ProjectGlobalVariableSchema CreateSingleDoubleSchema(Guid variableId, double initialValue)
    {
        return new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "stats.score",
                    DisplayName = "Score",
                    ValueType = ProjectGlobalVariableValueType.Double,
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

    private static IOperatorExecutor CreatePayloadSourceExecutor(Operator source, object? payload)
    {
        var sourceExecutor = Substitute.For<IOperatorExecutor>();
        sourceExecutor.OperatorType.Returns(OperatorType.Thresholding);
        sourceExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());
        sourceExecutor.ExecuteAsync(source, Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Payload"] = payload!
            })));
        return sourceExecutor;
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

    private sealed class Image
    {
    }
}
