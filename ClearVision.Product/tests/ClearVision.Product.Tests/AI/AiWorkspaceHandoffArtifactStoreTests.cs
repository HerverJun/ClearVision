using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Handoff;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

public sealed class AiWorkspaceHandoffArtifactStoreTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "clearvision-ai-handoff-test-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 7, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ShouldBeOwnerOperationIdempotentAndDetectCandidateConflict()
    {
        var store = CreateStore();
        var command = CreateCommand();

        var created = store.Create(command);
        var replay = store.Create(command);
        var conflict = store.Create(command with { CandidateFlowFingerprint = new string('B', 64) });

        created.Outcome.Should().Be(AiWorkspaceHandoffStoreOutcome.Created);
        replay.Outcome.Should().Be(AiWorkspaceHandoffStoreOutcome.Existing);
        replay.Artifact!.ArtifactId.Should().Be(created.Artifact!.ArtifactId);
        conflict.Outcome.Should().Be(AiWorkspaceHandoffStoreOutcome.IdentityConflict);
        conflict.ErrorCode.Should().Be("handoff_create_identity_conflict");
    }

    [Fact]
    public void Artifact_ShouldSurviveReloadAndExpireFailClosed()
    {
        var command = CreateCommand();
        var created = CreateStore().Create(command).Artifact!;

        var reloaded = CreateStore();
        reloaded.Get(command.OwnerHash, created.ArtifactId).Should().BeEquivalentTo(created);

        _now = _now.Add(AiWorkspaceHandoffArtifactStore.ArtifactTimeToLive).Add(TimeSpan.FromSeconds(1));
        var expired = reloaded.Get(command.OwnerHash, created.ArtifactId);

        expired!.Status.Should().Be(AiWorkspaceHandoffStatuses.Expired);
        reloaded.ReserveConsume(command.OwnerHash, created.ArtifactId, Guid.NewGuid(), null)
            .Outcome.Should().Be(AiWorkspaceHandoffStoreOutcome.Expired);
        CreateStore().Get(command.OwnerHash, created.ArtifactId)!.Status
            .Should().Be(AiWorkspaceHandoffStatuses.Expired);
    }

    [Fact]
    public void Consume_ShouldBeTwoPhaseIdempotentAndRejectDifferentIdentity()
    {
        var store = CreateStore();
        var command = CreateCommand();
        var artifact = store.Create(command).Artifact!;
        var consumeId = Guid.NewGuid();

        var reserved = store.ReserveConsume(command.OwnerHash, artifact.ArtifactId, consumeId, null);
        var reserveReplay = store.ReserveConsume(command.OwnerHash, artifact.ArtifactId, consumeId, null);
        var reserveConflict = store.ReserveConsume(command.OwnerHash, artifact.ArtifactId, Guid.NewGuid(), null);
        var consumed = store.Acknowledge(command.OwnerHash, artifact.ArtifactId, consumeId, null);
        var acknowledgeReplay = store.Acknowledge(command.OwnerHash, artifact.ArtifactId, consumeId, null);
        var acknowledgeConflict = store.Acknowledge(command.OwnerHash, artifact.ArtifactId, Guid.NewGuid(), null);

        reserved.Outcome.Should().Be(AiWorkspaceHandoffStoreOutcome.Updated);
        reserved.Artifact!.Status.Should().Be(AiWorkspaceHandoffStatuses.Consuming);
        reserveReplay.Outcome.Should().Be(AiWorkspaceHandoffStoreOutcome.Existing);
        reserveConflict.Outcome.Should().Be(AiWorkspaceHandoffStoreOutcome.IdentityConflict);
        consumed.Artifact!.Status.Should().Be(AiWorkspaceHandoffStatuses.Consumed);
        consumed.Artifact.ConsumeReceipt!.ProjectSaved.Should().BeFalse();
        acknowledgeReplay.Outcome.Should().Be(AiWorkspaceHandoffStoreOutcome.Existing);
        acknowledgeReplay.Artifact!.ConsumeReceipt.Should().Be(consumed.Artifact.ConsumeReceipt);
        acknowledgeConflict.Outcome.Should().Be(AiWorkspaceHandoffStoreOutcome.IdentityConflict);
    }

    [Fact]
    public void ConsumingArtifact_ShouldRemainRecoverableAfterCrash()
    {
        var store = CreateStore();
        var command = CreateCommand();
        var artifact = store.Create(command).Artifact!;
        var consumeId = Guid.NewGuid();
        store.ReserveConsume(command.OwnerHash, artifact.ArtifactId, consumeId, null)
            .Outcome.Should().Be(AiWorkspaceHandoffStoreOutcome.Updated);

        var reloaded = CreateStore();
        var lookup = reloaded.Get(command.OwnerHash, artifact.ArtifactId);
        var acknowledged = reloaded.Acknowledge(command.OwnerHash, artifact.ArtifactId, consumeId, null);

        lookup!.Status.Should().Be(AiWorkspaceHandoffStatuses.Consuming);
        lookup.ConsumeClientOperationId.Should().Be(consumeId);
        acknowledged.Artifact!.Status.Should().Be(AiWorkspaceHandoffStatuses.Consumed);
    }

    [Fact]
    public void Create_ShouldEnforceOwnerCapacityWithoutEvictingActiveArtifacts()
    {
        var store = CreateStore();
        var owner = "usr_" + new string('a', 64);
        var created = Enumerable.Range(0, AiWorkspaceHandoffArtifactStore.MaxActiveArtifactsPerOwner)
            .Select(index => store.Create(CreateCommand() with
            {
                OwnerHash = owner,
                ClientOperationId = Guid.NewGuid(),
                BuildRunId = $"ar_capacity_{index}",
                BuildIdentity = $"build_capacity_{index}"
            }))
            .ToArray();

        created.Should().OnlyContain(result => result.Outcome == AiWorkspaceHandoffStoreOutcome.Created);
        var overflow = store.Create(CreateCommand() with
        {
            OwnerHash = owner,
            ClientOperationId = Guid.NewGuid(),
            BuildRunId = "ar_capacity_overflow",
            BuildIdentity = "build_capacity_overflow"
        });
        overflow.Outcome.Should().Be(AiWorkspaceHandoffStoreOutcome.CapacityExceeded);
    }

    private AiWorkspaceHandoffArtifactStore CreateStore() => new(_tempRoot, () => _now);

    private static AiWorkspaceHandoffCreateCommand CreateCommand() => new()
    {
        OwnerHash = "usr_" + new string('a', 64),
        ClientOperationId = Guid.NewGuid(),
        SessionId = "session-handoff-1",
        SessionRevision = 12,
        PlanRunId = "ar_plan_1",
        PlanId = "plan-1",
        PlanHash = new string('1', 64),
        BuildRunId = "ar_build_1",
        BuildClientOperationId = Guid.NewGuid(),
        BuildIdentity = "build-identity-1",
        SubmittedBuildFingerprint = new string('2', 64),
        AnswerRevision = 2,
        ResourceRevision = 1,
        TargetKind = "new",
        ProjectBaseline = new AiProjectBaselineIdentity { TargetKind = "new" },
        CandidateFlowJson = "{\"id\":\"00000000-0000-4000-8000-000000000001\",\"name\":\"MainFlow\",\"operators\":[],\"connections\":[],\"decisionConfiguration\":null}",
        CandidateFlowFingerprint = new string('A', 64),
        PublicBuild = new VisionAgentPublicBuildResultV1
        {
            RunId = "ar_build_1",
            BuildId = "build-1",
            BuildIdentity = "build-identity-1",
            CandidateFlowFingerprint = new string('A', 64),
            MetadataOnly = true,
            RedactionPass = true
        }
    };

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }
}
