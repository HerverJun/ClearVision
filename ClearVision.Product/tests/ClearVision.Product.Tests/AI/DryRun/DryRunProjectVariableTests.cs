using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.AI.DryRun;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Tests.AI.DryRun;

public sealed class DryRunProjectVariableTests
{
    [Fact]
    public async Task RunAsync_WhenProjectVariableContextProvided_ShouldUsePreviewCloneAndKeepFormalSession()
    {
        var variableId = Guid.NewGuid();
        var accessor = new ProjectVariableExecutionContextAccessor();
        var increment = new Operator(Guid.NewGuid(), "Increment", OperatorType.VariableIncrement, 0, 0);
        increment.AddParameter(new Parameter(Guid.NewGuid(), "Scope", "Scope", "", "enum", "Project"));
        increment.AddParameter(new Parameter(Guid.NewGuid(), "VariableId", "VariableId", "", "string", variableId.ToString()));
        increment.AddParameter(new Parameter(Guid.NewGuid(), "VariableName", "VariableName", "", "string", "stats.count"));
        increment.AddParameter(new Parameter(Guid.NewGuid(), "Delta", "Delta", "", "int", 5));
        var flow = new OperatorFlow("dryrun-project-variable");
        flow.AddOperator(increment);
        var schema = CreateSchema(variableId, 1L);
        using var session = new ProjectVariableSession(schema);
        session.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var context = new ProjectVariableExecutionContext(
            session,
            ProjectVariableBindingIndex.Build(schema),
            Guid.NewGuid());
        var flowExecution = new FlowExecutionService(
            [
                new VariableIncrementOperator(
                    NullLogger<VariableIncrementOperator>.Instance,
                    new VariableContext(),
                    accessor)
            ],
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext(),
            accessor);
        var service = new DryRunService(flowExecution);

        var result = await service.RunAsync(
            flow,
            new Dictionary<string, object>(),
            new DryRunStubRegistry(),
            context);

        result.IsSuccess.Should().BeTrue(result.FlowResult?.ErrorMessage);
        result.FlowResult!.OutputData.Should().ContainKey("NewValue");
        Convert.ToInt64(result.FlowResult.OutputData!["NewValue"]).Should().Be(9L);
        session.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(4L);
        formal.Version.Should().Be(1);
        formal.UpdatedBy.Should().Be(ProjectVariableUpdatedBy.StudioManual);
    }

    [Fact]
    public async Task RunAsync_WhenProjectVariableContextOmitted_ShouldKeepLegacyExecutionPath()
    {
        var flow = new OperatorFlow("dryrun-legacy");
        var flowExecution = Substitute.For<IFlowExecutionService>();
        flowExecution.ExecuteFlowAsync(
                flow,
                Arg.Any<Dictionary<string, object>?>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowExecutionResult
            {
                IsSuccess = true,
                OutputData = new Dictionary<string, object>()
            }));
        var service = new DryRunService(flowExecution);

        var result = await service.RunAsync(
            flow,
            new Dictionary<string, object>(),
            new DryRunStubRegistry());

        result.IsSuccess.Should().BeTrue();
        await flowExecution.Received(1).ExecuteFlowAsync(
            flow,
            Arg.Any<Dictionary<string, object>?>(),
            false,
            Arg.Any<CancellationToken>());
    }

    private static ProjectGlobalVariableSchema CreateSchema(Guid variableId, long initialValue)
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
}
