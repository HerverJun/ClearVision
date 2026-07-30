using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

public sealed class AiOperationReceiptStoreTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "clearvision-ai-operation-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Reserve_ShouldBeOwnerKindScopedIdempotentAndDetectPayloadConflict()
    {
        var store = new AiOperationReceiptStore(_tempRoot);
        var operationId = Guid.NewGuid();
        var owner = "usr_" + new string('a', 64);

        var first = store.Reserve(owner, AiOperationKinds.PlanRun, operationId, "sha256:" + new string('1', 64));
        var replay = store.Reserve(owner, AiOperationKinds.PlanRun, operationId, "sha256:" + new string('1', 64));
        var conflict = store.Reserve(owner, AiOperationKinds.PlanRun, operationId, "sha256:" + new string('2', 64));
        var otherKind = store.Reserve(owner, AiOperationKinds.BuildRun, operationId, "sha256:" + new string('2', 64));
        var otherOwner = store.Reserve("usr_" + new string('b', 64), AiOperationKinds.PlanRun,
            operationId, "sha256:" + new string('2', 64));

        first.Outcome.Should().Be(AiOperationReservationOutcome.Reserved);
        replay.Outcome.Should().Be(AiOperationReservationOutcome.Existing);
        replay.Receipt.Should().Be(first.Receipt);
        conflict.Outcome.Should().Be(AiOperationReservationOutcome.IdentityConflict);
        conflict.ErrorCode.Should().Be("operation_identity_conflict");
        otherKind.Outcome.Should().Be(AiOperationReservationOutcome.Reserved);
        otherOwner.Outcome.Should().Be(AiOperationReservationOutcome.Reserved);
    }

    [Fact]
    public void CreatedReceipt_ShouldSurviveReloadWithConfirmedProjectBaseline()
    {
        var store = new AiOperationReceiptStore(_tempRoot);
        var operationId = Guid.NewGuid();
        var owner = "usr_" + new string('a', 64);
        var baseline = new AiProjectBaselineIdentity
        {
            TargetKind = "existing",
            ProjectId = Guid.NewGuid(),
            PersistenceRevision = 8,
            CanonicalFlowHash = new string('A', 64)
        };
        store.Reserve(owner, AiOperationKinds.BuildRun, operationId, "sha256:" + new string('3', 64));
        store.MarkCreated(
                owner,
                AiOperationKinds.BuildRun,
                operationId,
                "session-1",
                "ar_1",
                baseline,
                "artifact-1")
            .Should().NotBeNull();

        var reloaded = new AiOperationReceiptStore(_tempRoot);
        var receipt = reloaded.Get(owner, AiOperationKinds.BuildRun, operationId);

        receipt!.Status.Should().Be(AiOperationStatuses.Created);
        receipt.SessionId.Should().Be("session-1");
        receipt.RunId.Should().Be("ar_1");
        receipt.ArtifactId.Should().Be("artifact-1");
        receipt.ProjectBaseline.Should().BeEquivalentTo(baseline);
        reloaded.FindByRun(owner, AiOperationKinds.BuildRun, "ar_1").Should().Be(receipt);
    }

    [Fact]
    public async Task ConcurrentReserve_ShouldHaveExactlyOneWinner()
    {
        var store = new AiOperationReceiptStore(_tempRoot);
        var operationId = Guid.NewGuid();
        var owner = "usr_" + new string('a', 64);
        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
            store.Reserve(owner, AiOperationKinds.PlanRun, operationId, "sha256:" + new string('4', 64))));

        var results = await Task.WhenAll(tasks);

        results.Should().ContainSingle(result => result.Outcome == AiOperationReservationOutcome.Reserved);
        results.Count(result => result.Outcome == AiOperationReservationOutcome.Existing).Should().Be(19);
    }

    [Fact]
    public void Reserve_ShouldRetryTransientDestinationSharingViolation()
    {
        var store = new AiOperationReceiptStore(_tempRoot);
        var owner = "usr_" + new string('a', 64);
        store.Reserve(
                owner,
                AiOperationKinds.SessionCreate,
                Guid.NewGuid(),
                "sha256:" + new string('5', 64))
            .Outcome.Should().Be(AiOperationReservationOutcome.Reserved);

        var storagePath = Path.Combine(_tempRoot, "ai_operation_receipts.json");
        using var destinationLock = new FileStream(
            storagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
        var lockReleaser = new Thread(() =>
        {
            Thread.Sleep(90);
            destinationLock.Dispose();
        })
        {
            IsBackground = true
        };
        lockReleaser.Start();

        var result = store.Reserve(
            owner,
            AiOperationKinds.PlanRun,
            Guid.NewGuid(),
            "sha256:" + new string('6', 64));

        result.Outcome.Should().Be(AiOperationReservationOutcome.Reserved);
        lockReleaser.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }
}
