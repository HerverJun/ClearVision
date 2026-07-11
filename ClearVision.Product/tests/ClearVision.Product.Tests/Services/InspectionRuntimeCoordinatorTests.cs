using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Tests.Services;

public class InspectionRuntimeCoordinatorTests
{
    [Fact]
    public async Task TryStartAsync_ForSameProjectTwice_ReturnsAlreadyRunningOnSecondCall()
    {
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var projectId = Guid.NewGuid();

        var first = await coordinator.TryStartAsync(projectId, Guid.NewGuid(), CancellationToken.None);
        var second = await coordinator.TryStartAsync(projectId, Guid.NewGuid(), CancellationToken.None);

        first.Should().Be(StartResult.Success);
        second.Should().Be(StartResult.AlreadyRunning);
        coordinator.GetState(projectId)!.Status.Should().Be(RuntimeStatus.Starting);
    }

    [Fact]
    public async Task MarkAsStopped_RemovesStateAfterScheduledCleanup()
    {
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var start = await coordinator.TryStartAsync(projectId, sessionId, CancellationToken.None);
        start.Should().Be(StartResult.Success);

        coordinator.MarkAsStopped(projectId, sessionId);

        var started = DateTime.UtcNow;
        while (coordinator.GetState(projectId) != null)
        {
            if (DateTime.UtcNow - started > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("Coordinator cleanup did not remove the session in time.");
            }

            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task StaleSessionCleanup_DoesNotRemoveReplacementSession()
    {
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var projectId = Guid.NewGuid();
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();

        (await coordinator.TryStartAsync(projectId, firstSessionId, CancellationToken.None))
            .Should().Be(StartResult.Success);

        coordinator.MarkAsStopped(projectId, firstSessionId);

        (await coordinator.TryStartAsync(projectId, secondSessionId, CancellationToken.None))
            .Should().Be(StartResult.Success);

        await Task.Delay(200);

        var replacementState = coordinator.GetState(projectId);
        replacementState.Should().NotBeNull();
        replacementState!.SessionId.Should().Be(secondSessionId);
        replacementState.Status.Should().Be(RuntimeStatus.Starting);

        coordinator.MarkAsStopped(projectId, firstSessionId);

        coordinator.GetState(projectId)!.SessionId.Should().Be(secondSessionId);
    }

    [Theory]
    [InlineData(RuntimeStatus.Stopped)]
    [InlineData(RuntimeStatus.Faulted)]
    public async Task TryStartAsync_AfterTerminalStateBeforeAsyncCleanup_ShouldStartReplacement(RuntimeStatus terminalStatus)
    {
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var projectId = Guid.NewGuid();
        var firstSessionId = Guid.NewGuid();
        var replacementSessionId = Guid.NewGuid();

        (await coordinator.TryStartAsync(projectId, firstSessionId, CancellationToken.None))
            .Should().Be(StartResult.Success);
        if (terminalStatus == RuntimeStatus.Stopped)
        {
            coordinator.MarkAsStopped(projectId, firstSessionId);
        }
        else
        {
            coordinator.MarkAsFaulted(projectId, firstSessionId, "synthetic fault");
        }

        var restart = await coordinator.TryStartAsync(projectId, replacementSessionId, CancellationToken.None);

        restart.Should().Be(StartResult.Success);
        coordinator.GetState(projectId)!.SessionId.Should().Be(replacementSessionId);
        coordinator.GetState(projectId)!.Status.Should().Be(RuntimeStatus.Starting);
    }

    [Fact]
    public async Task TryStartAsync_WhenStopping_ShouldRejectDuplicateStart()
    {
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        (await coordinator.TryStartAsync(projectId, sessionId, CancellationToken.None))
            .Should().Be(StartResult.Success);
        coordinator.UpdateSessionStatus(projectId, sessionId, RuntimeStatus.Stopping);

        var duplicate = await coordinator.TryStartAsync(projectId, Guid.NewGuid(), CancellationToken.None);

        duplicate.Should().Be(StartResult.AlreadyRunning);
        coordinator.GetState(projectId)!.SessionId.Should().Be(sessionId);
        coordinator.GetState(projectId)!.Status.Should().Be(RuntimeStatus.Stopping);
    }

    [Fact]
    public async Task TryAcquireMutationLease_WhenRunActive_ShouldReturnNull()
    {
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var projectId = Guid.NewGuid();

        (await coordinator.TryStartAsync(projectId, Guid.NewGuid(), CancellationToken.None))
            .Should().Be(StartResult.Success);

        var lease = await coordinator.TryAcquireMutationLeaseAsync(projectId, "schema-save", CancellationToken.None);

        lease.Should().BeNull();
    }

    [Fact]
    public async Task TryStartAsync_WhenMutationLeaseActive_ShouldReturnMutationInProgress()
    {
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var projectId = Guid.NewGuid();

        await using var lease = await coordinator.TryAcquireMutationLeaseAsync(projectId, "schema-save", CancellationToken.None);
        lease.Should().NotBeNull();

        var result = await coordinator.TryStartAsync(projectId, Guid.NewGuid(), CancellationToken.None);

        result.Should().Be(StartResult.MutationInProgress);
    }

    [Fact]
    public async Task StateChanged_WhenSubscriberThrows_ShouldContinueStateTransitionAndNotifyRemainingSubscribers()
    {
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var projectId = Guid.NewGuid();
        var throwingSubscriberCalls = 0;
        var healthySubscriberCalls = 0;

        coordinator.StateChanged += (_, _) =>
        {
            throwingSubscriberCalls++;
            throw new InvalidOperationException("synthetic subscriber failure");
        };
        coordinator.StateChanged += (_, args) =>
        {
            args.NewStatus.Should().Be(RuntimeStatus.Starting);
            healthySubscriberCalls++;
        };

        var result = await coordinator.TryStartAsync(projectId, Guid.NewGuid(), CancellationToken.None);

        result.Should().Be(StartResult.Success);
        coordinator.GetState(projectId)!.Status.Should().Be(RuntimeStatus.Starting);
        throwingSubscriberCalls.Should().Be(1);
        healthySubscriberCalls.Should().Be(1);
    }

    [Fact]
    public async Task StateChanged_WhenSubscriberStopsStartingRun_ShouldNotBeInvokedInsideStateLock()
    {
        var coordinator = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var reentrantStopCompleted = false;

        coordinator.StateChanged += (_, args) =>
        {
            if (args.NewStatus != RuntimeStatus.Starting)
            {
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            coordinator.TryStopAsync(projectId, timeout.Token).GetAwaiter().GetResult();
            reentrantStopCompleted = true;
        };

        var result = await coordinator.TryStartAsync(projectId, sessionId, CancellationToken.None);

        result.Should().Be(StartResult.Success);
        reentrantStopCompleted.Should().BeTrue();
        coordinator.GetState(projectId)!.Status.Should().Be(RuntimeStatus.Stopping);
    }
}
