using System.Collections.Concurrent;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.AI.DryRun;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using NSubstitute;

namespace ClearVision.Product.Tests.AI.DryRun;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "execution-authority", Suites = "ServicesRegression")]
public sealed class DryRunAuthorityTests
{
    [Fact]
    public async Task RunAsync_ExternalSideEffectFlow_ShouldRejectBeforeEngineValidationOrDispatch()
    {
        var networkOperator = new Operator(
            Guid.NewGuid(),
            "HTTP side effect",
            OperatorType.HttpRequest,
            0,
            0);
        networkOperator.AddParameter(new Parameter(
            Guid.NewGuid(),
            "Url",
            "Url",
            string.Empty,
            "string",
            "https://client-forged.example/api"));
        var flow = new OperatorFlow("unsafe-dry-run");
        flow.AddOperator(networkOperator);
        var engine = Substitute.For<IFlowExecutionEngine>();
        var service = new DryRunService(new GovernedFlowExecutionService(engine));

        var result = await service.RunAsync(
            flow,
            new Dictionary<string, object>(),
            new DryRunStubRegistry());

        result.IsSuccess.Should().BeFalse();
        result.FlowResult!.ErrorMessage.Should().Contain("ADMISSION_DRAFT_PREVIEW_SIDE_EFFECT_BLOCKED");
        engine.DidNotReceive().ValidateFlow(Arg.Any<OperatorFlow>());
        await engine.DidNotReceive().ExecuteFlowAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_InputCountExceedsHardLimit_ShouldRejectBeforeValidationOrDispatch()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        var inputs = Enumerable.Range(0, DryRunService.MaxInputCount + 1)
            .ToDictionary(index => $"input-{index}", index => (object)index);
        var service = new DryRunService(flowExecution);

        var result = await service.RunAsync(
            new OperatorFlow("bounded-inputs"),
            inputs,
            new DryRunStubRegistry());

        result.IsSuccess.Should().BeFalse();
        result.FlowResult!.ErrorMessage.Should().Contain("ADMISSION_DRY_RUN_INPUT_BOUNDS");
        flowExecution.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ConcurrentRuns_ShouldUseDistinctAsyncLocalAuthorityContexts()
    {
        var flowExecution = Substitute.For<IFlowExecutionService>();
        flowExecution.ValidateSnapshot(Arg.Any<ExecutionSnapshot>())
            .Returns(new FlowValidationResult { IsValid = true });
        var contexts = new ConcurrentDictionary<string, DryRunContext>();
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        flowExecution.ExecuteWithSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<Dictionary<string, object>?>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var inputs = callInfo.ArgAt<Dictionary<string, object>?>(1)!;
                var marker = (string)inputs["marker"];
                contexts[marker] = DryRunContext.Current
                    ?? throw new InvalidOperationException("Dry-run context was not installed.");
                if (Interlocked.Increment(ref entered) == 2)
                {
                    bothEntered.TrySetResult();
                }

                await bothEntered.Task;
                DryRunContext.Current.Should().BeSameAs(contexts[marker]);
                return new FlowExecutionResult { IsSuccess = true };
            });
        var service = new DryRunService(flowExecution);
        var flow = new OperatorFlow("concurrent-dry-run");

        var first = service.RunAsync(
            flow,
            new Dictionary<string, object> { ["marker"] = "first" },
            new DryRunStubRegistry());
        var second = service.RunAsync(
            flow,
            new Dictionary<string, object> { ["marker"] = "second" },
            new DryRunStubRegistry());
        var results = await Task.WhenAll(first, second);

        results.Should().OnlyContain(result => result.IsSuccess);
        contexts.Should().ContainKeys("first", "second");
        contexts["first"].Should().NotBeSameAs(contexts["second"]);
        contexts["first"].SessionId.Should().NotBe(contexts["second"].SessionId);
        contexts["first"].RunId.Should().NotBe(contexts["second"].RunId);
        DryRunContext.Current.Should().BeNull();
    }
}
