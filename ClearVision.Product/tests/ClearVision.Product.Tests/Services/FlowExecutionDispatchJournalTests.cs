using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.Runtime;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
[Collection(RuntimeConcurrencyCollection.Name)]
public sealed class FlowExecutionDispatchJournalTests
{
    [Fact]
    public async Task CancellationWhilePrepared_ShouldInvokeZeroHandlersAndRecordFailedPreparation()
    {
        var journal = new BarrierDispatchJournal(blockBeforeDispatch: true);
        var executor = new CancellationAwareExecutor(waitForCancellation: false);
        using var service = CreateService(executor, journal);
        using var cancellation = new CancellationTokenSource();
        using var authority = EnterAuthority();

        var execution = service.ExecuteOperatorAsync(CreateSideEffectOperator(), cancellationToken: cancellation.Token);
        await journal.DispatchTransitionEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(2));

        executor.CallCount.Should().Be(0);
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("canceled");
        var entry = journal.Entries.Should().ContainSingle().Subject;
        entry.Stage.Should().Be(OperatorDispatchStage.Prepared);
        entry.Outcome.Should().Be(OperatorDispatchOutcome.Failed);
        entry.FailureCode.Should().Be("DISPATCH_CANCELED_BEFORE_HANDLER");
    }

    [Fact]
    public async Task CancellationAfterHandlerDispatch_ShouldPersistIndeterminateWithoutRollbackClaim()
    {
        var journal = new BarrierDispatchJournal(blockBeforeDispatch: false);
        var executor = new CancellationAwareExecutor(waitForCancellation: true);
        using var service = CreateService(executor, journal);
        using var cancellation = new CancellationTokenSource();
        var scope = CreateAuthorityScope();
        using var authority = ExecutionAuthorityContext.Enter(scope);

        var execution = service.ExecuteOperatorAsync(CreateSideEffectOperator(), cancellationToken: cancellation.Token);
        await executor.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(2));

        executor.CallCount.Should().Be(1);
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("canceled").And.NotContain("rollback");
        var entry = journal.Entries.Should().ContainSingle().Subject;
        entry.CorrelationId.Should().NotBeEmpty();
        entry.ProjectId.Should().Be(scope.ProjectId);
        entry.SessionId.Should().Be(scope.SessionId);
        entry.FlowId.Should().Be(scope.FlowId);
        entry.RunId.Should().Be(scope.RunId);
        entry.OperatorId.Should().NotBeEmpty();
        entry.ResourceBindingFingerprint.Should().HaveLength(64);
        entry.ResourceBindingFingerprint.Should().NotContain("secret-device-target");
        entry.Stage.Should().Be(OperatorDispatchStage.Dispatched);
        entry.Outcome.Should().Be(OperatorDispatchOutcome.Indeterminate);
        entry.FailureCode.Should().Be("DISPATCH_CANCELED_OUTCOME_UNKNOWN");
    }

    private static FlowExecutionService CreateService(
        IOperatorExecutor executor,
        IOperatorDispatchJournal journal) =>
        new(
            [executor],
            Substitute.For<ILogger<FlowExecutionService>>(),
            Substitute.For<IVariableContext>(),
            operatorDispatchJournal: journal);

    private static Operator CreateSideEffectOperator() =>
        new(Guid.NewGuid(), "HTTP side effect", OperatorType.HttpRequest, 0, 0);

    private static IDisposable EnterAuthority() =>
        ExecutionAuthorityContext.Enter(CreateAuthorityScope());

    private static ExecutionAuthorityScope CreateAuthorityScope() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ExecutionSnapshotSource.PersistedProject,
            ExecutionRunMode.FormalPrimary,
            "flow-hash",
            new ExecutionPrincipal("operator-1", "Operator", "Operator", true),
            new Dictionary<string, string>
            {
                ["HttpProfile"] = "secret-device-target",
                ["ProjectRevision"] = "42"
            });

    private sealed class CancellationAwareExecutor(bool waitForCancellation) : IOperatorExecutor
    {
        private int _callCount;

        public OperatorType OperatorType => OperatorType.HttpRequest;
        public int CallCount => Volatile.Read(ref _callCount);
        public TaskCompletionSource<bool> Invoked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<OperatorExecutionOutput> ExecuteAsync(
            Operator @operator,
            Dictionary<string, object>? inputs = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            Invoked.TrySetResult(true);
            if (waitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return OperatorExecutionOutput.Success();
        }

        public ValidationResult ValidateParameters(Operator @operator) => ValidationResult.Valid();
    }

    private sealed class BarrierDispatchJournal(bool blockBeforeDispatch) : IOperatorDispatchJournal
    {
        private readonly InMemoryOperatorDispatchJournal _inner = new();

        public IReadOnlyCollection<OperatorDispatchJournalEntry> Entries => _inner.Entries;
        public TaskCompletionSource<bool> DispatchTransitionEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<OperatorDispatchJournalEntry> PrepareAsync(
            OperatorDispatchIdentity identity,
            CancellationToken cancellationToken = default) =>
            _inner.PrepareAsync(identity, cancellationToken);

        public async ValueTask<OperatorDispatchJournalEntry> MarkDispatchedAsync(
            Guid correlationId,
            CancellationToken cancellationToken = default)
        {
            DispatchTransitionEntered.TrySetResult(true);
            if (blockBeforeDispatch)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return await _inner.MarkDispatchedAsync(correlationId, cancellationToken);
        }

        public ValueTask<OperatorDispatchJournalEntry> MarkPreparedFailedAsync(
            Guid correlationId,
            string failureCode,
            CancellationToken cancellationToken = default) =>
            _inner.MarkPreparedFailedAsync(correlationId, failureCode, cancellationToken);

        public ValueTask<OperatorDispatchJournalEntry> MarkIndeterminateAsync(
            Guid correlationId,
            string failureCode,
            CancellationToken cancellationToken = default) =>
            _inner.MarkIndeterminateAsync(correlationId, failureCode, cancellationToken);

        public ValueTask<OperatorDispatchJournalEntry> ConfirmAsync(
            Guid correlationId,
            OperatorDispatchOutcome outcome,
            string? failureCode = null,
            CancellationToken cancellationToken = default) =>
            _inner.ConfirmAsync(correlationId, outcome, failureCode, cancellationToken);

        public ValueTask<OperatorDispatchJournalEntry?> TryGetAsync(
            Guid correlationId,
            CancellationToken cancellationToken = default) =>
            _inner.TryGetAsync(correlationId, cancellationToken);
    }
}
