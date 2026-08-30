using ClearVision.Product.Infrastructure.AI;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public class PromptVersionManagerTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    [Fact]
    public async Task CreateActivateAndRecordMetrics_ShouldPersistAcrossReload()
    {
        var tempDir = CreateTempDir();
        var sut = new PromptVersionManager(tempDir);

        var version1 = await sut.CreateVersionAsync("V1", "prompt-1", "baseline", "tester");
        var version2 = await sut.CreateVersionAsync("V2", "prompt-2", "optimized", "tester");

        await sut.ActivateVersionAsync(version2.Id);
        await sut.RecordMetricsAsync(version2.Id, success: true, tokenUsage: 120, latencyMs: 40);
        await sut.RecordMetricsAsync(version2.Id, success: false, tokenUsage: 30, latencyMs: 10);

        var reloaded = new PromptVersionManager(tempDir);
        var active = await reloaded.GetActiveVersionAsync();

        active.Id.Should().Be(version2.Id);
        active.Metrics.TotalCalls.Should().Be(2);
        active.Metrics.SuccessCalls.Should().Be(1);
        active.Metrics.TotalTokenUsage.Should().Be(150);
        active.Metrics.TotalLatencyMs.Should().Be(50);
    }

    [Fact]
    public async Task DeleteVersionAsync_WhenDeletingActive_ShouldPromoteNewestRemaining()
    {
        var tempDir = CreateTempDir();
        var sut = new PromptVersionManager(tempDir);

        var version1 = await sut.CreateVersionAsync("V1", "prompt-1", "baseline", "tester");
        await Task.Delay(20);
        var version2 = await sut.CreateVersionAsync("V2", "prompt-2", "optimized", "tester");

        await sut.ActivateVersionAsync(version1.Id);
        await sut.DeleteVersionAsync(version1.Id);

        var active = await sut.GetActiveVersionAsync();
        active.Id.Should().Be(version2.Id);

        var versions = await sut.ListVersionsAsync();
        versions.Should().ContainSingle(v => v.Id == version2.Id);
    }

    [Fact]
    public async Task ConcurrentMetrics_ShouldSerializeFullReadModifyCandidatePersistCycle()
    {
        var tempDir = CreateTempDir();
        var faultInjector = new AiPersistenceTestFaultInjector();
        var health = new AiAuxiliaryPersistenceHealth();
        var firstManager = new PromptVersionManager(tempDir, faultInjector, health);
        var secondManager = new PromptVersionManager(tempDir, faultInjector, health);
        var version = await firstManager.CreateVersionAsync("V1", "prompt", "baseline", "tester");
        using var firstCandidateEntered = new ManualResetEventSlim(false);
        using var releaseFirstCandidate = new ManualResetEventSlim(false);
        var candidates = new List<string>();
        var candidateCount = 0;
        faultInjector.SetHandler((stage, authority, path) =>
        {
            if (stage != AiPersistenceStage.JsonCandidatePrepared || authority != "prompt_versions")
            {
                return;
            }

            lock (candidates)
            {
                candidates.Add(path);
            }

            if (Interlocked.Increment(ref candidateCount) == 1)
            {
                firstCandidateEntered.Set();
                releaseFirstCandidate.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            }
        });

        var first = Task.Run(() => firstManager.RecordMetricsAsync(version.Id, true, 10, 20));
        firstCandidateEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        var second = Task.Run(() => secondManager.RecordMetricsAsync(version.Id, false, 30, 40));
        await Task.Delay(100);
        second.IsCompleted.Should().BeFalse();
        releaseFirstCandidate.Set();
        await Task.WhenAll(first, second);

        var reloaded = new PromptVersionManager(tempDir);
        var persisted = await reloaded.GetVersionAsync(version.Id);
        persisted!.Metrics.TotalCalls.Should().Be(2);
        persisted.Metrics.SuccessCalls.Should().Be(1);
        persisted.Metrics.TotalTokenUsage.Should().Be(40);
        persisted.Metrics.TotalLatencyMs.Should().Be(60);
        candidates.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ConcurrentMetricsHealth_FailureThenSuccessAcrossManagers_ShouldRecoverInDurableOrder()
    {
        var tempDir = CreateTempDir();
        var faultInjector = new AiPersistenceTestFaultInjector();
        var health = new AiAuxiliaryPersistenceHealth();
        var firstManager = new PromptVersionManager(tempDir, faultInjector, health);
        var secondManager = new PromptVersionManager(tempDir, faultInjector, health);
        var version = await firstManager.CreateVersionAsync("V1", "prompt", "baseline", "tester");
        using var failedCandidateEntered = new ManualResetEventSlim(false);
        using var releaseFailedCandidate = new ManualResetEventSlim(false);
        using var successfulCandidateEntered = new ManualResetEventSlim(false);
        using var releaseSuccessfulCandidate = new ManualResetEventSlim(false);
        using var successfulWriteStarted = new ManualResetEventSlim(false);
        var candidateCount = 0;
        faultInjector.SetHandler((stage, authority, _) =>
        {
            if (stage != AiPersistenceStage.JsonCandidatePrepared || authority != "prompt_versions")
            {
                return;
            }

            var candidateNumber = Interlocked.Increment(ref candidateCount);
            if (candidateNumber == 1)
            {
                failedCandidateEntered.Set();
                releaseFailedCandidate.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                throw new IOException("first metrics commit failed");
            }

            if (candidateNumber == 2)
            {
                successfulCandidateEntered.Set();
                releaseSuccessfulCandidate.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            }
        });

        var failedWrite = Task.Run(() => firstManager.RecordMetricsAsync(version.Id, false, 10, 20));
        failedCandidateEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        var successfulWrite = Task.Run(async () =>
        {
            successfulWriteStarted.Set();
            await secondManager.RecordMetricsAsync(version.Id, true, 30, 40);
        });
        successfulWriteStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        successfulCandidateEntered.IsSet.Should().BeFalse();
        successfulWrite.IsCompleted.Should().BeFalse();

        releaseFailedCandidate.Set();
        successfulCandidateEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        await failedWrite;
        health.GetSnapshot().Degraded.Should().BeTrue(
            "the failed durable mutation must publish degradation before the later write can commit");

        releaseSuccessfulCandidate.Set();
        await successfulWrite;

        health.GetSnapshot().Degraded.Should().BeFalse();
        var persisted = await new PromptVersionManager(tempDir).GetVersionAsync(version.Id);
        persisted!.Metrics.TotalCalls.Should().Be(1);
        persisted.Metrics.SuccessCalls.Should().Be(1);
        persisted.Metrics.TotalTokenUsage.Should().Be(30);
        persisted.Metrics.TotalLatencyMs.Should().Be(40);
    }

    [Fact]
    public async Task ConcurrentMetricsHealth_SuccessThenFailureAcrossManagers_ShouldRemainDegradedInDurableOrder()
    {
        var tempDir = CreateTempDir();
        var faultInjector = new AiPersistenceTestFaultInjector();
        var health = new AiAuxiliaryPersistenceHealth();
        var firstManager = new PromptVersionManager(tempDir, faultInjector, health);
        var secondManager = new PromptVersionManager(tempDir, faultInjector, health);
        var version = await firstManager.CreateVersionAsync("V1", "prompt", "baseline", "tester");
        faultInjector.FailOnce(
            AiPersistenceStage.JsonCommitStarted,
            static () => new IOException("seed degraded health"));
        await firstManager.RecordMetricsAsync(version.Id, false, 1, 2);
        health.GetSnapshot().Degraded.Should().BeTrue();

        using var successfulCandidateEntered = new ManualResetEventSlim(false);
        using var releaseSuccessfulCandidate = new ManualResetEventSlim(false);
        using var failedCandidateEntered = new ManualResetEventSlim(false);
        using var releaseFailedCandidate = new ManualResetEventSlim(false);
        using var failedWriteStarted = new ManualResetEventSlim(false);
        var candidateCount = 0;
        faultInjector.SetHandler((stage, authority, _) =>
        {
            if (stage != AiPersistenceStage.JsonCandidatePrepared || authority != "prompt_versions")
            {
                return;
            }

            var candidateNumber = Interlocked.Increment(ref candidateCount);
            if (candidateNumber == 1)
            {
                successfulCandidateEntered.Set();
                releaseSuccessfulCandidate.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                return;
            }

            if (candidateNumber == 2)
            {
                failedCandidateEntered.Set();
                releaseFailedCandidate.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                throw new IOException("later metrics commit failed");
            }
        });

        var successfulWrite = Task.Run(() => firstManager.RecordMetricsAsync(version.Id, true, 50, 60));
        successfulCandidateEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        var failedWrite = Task.Run(async () =>
        {
            failedWriteStarted.Set();
            await secondManager.RecordMetricsAsync(version.Id, false, 70, 80);
        });
        failedWriteStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        failedCandidateEntered.IsSet.Should().BeFalse();
        failedWrite.IsCompleted.Should().BeFalse();
        health.GetSnapshot().Degraded.Should().BeTrue(
            "health cannot recover before the successful candidate is durably committed");

        releaseSuccessfulCandidate.Set();
        failedCandidateEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        await successfulWrite;
        health.GetSnapshot().Degraded.Should().BeFalse(
            "the successful durable mutation must recover health before the later write starts committing");

        releaseFailedCandidate.Set();
        await failedWrite;

        health.GetSnapshot().Degraded.Should().BeTrue();
        var persisted = await new PromptVersionManager(tempDir).GetVersionAsync(version.Id);
        persisted!.Metrics.TotalCalls.Should().Be(1);
        persisted.Metrics.SuccessCalls.Should().Be(1);
        persisted.Metrics.TotalTokenUsage.Should().Be(50);
        persisted.Metrics.TotalLatencyMs.Should().Be(60);
    }

    [Fact]
    public async Task MetricsCommitFailure_ShouldRecordDegradedRetryableHealthWithoutChangingEvidence()
    {
        var tempDir = CreateTempDir();
        var faultInjector = new AiPersistenceTestFaultInjector();
        var health = new AiAuxiliaryPersistenceHealth();
        var sut = new PromptVersionManager(tempDir, faultInjector, health);
        var version = await sut.CreateVersionAsync("V1", "prompt", "baseline", "tester");
        var before = File.ReadAllText(Path.Combine(tempDir, "prompt_versions.json"));
        faultInjector.FailOnce(
            AiPersistenceStage.JsonCommitStarted,
            static () => new IOException("metrics commit failed"));

        var act = () => sut.RecordMetricsAsync(version.Id, true, 11, 22);
        await act.Should().NotThrowAsync();

        File.ReadAllText(Path.Combine(tempDir, "prompt_versions.json")).Should().Be(before);
        var degraded = health.GetSnapshot();
        degraded.Degraded.Should().BeTrue();
        degraded.ActiveFailures.Should().ContainSingle(item =>
            item.Authority == "prompt_versions" &&
            item.Operation == "record_metrics" &&
            item.Retryable);

        await sut.RecordMetricsAsync(Guid.NewGuid(), true, 99, 99);
        health.GetSnapshot().Degraded.Should().BeTrue();

        await sut.RecordMetricsAsync(version.Id, true, 5, 6);
        health.GetSnapshot().Degraded.Should().BeFalse();
        var persisted = await new PromptVersionManager(tempDir).GetVersionAsync(version.Id);
        persisted!.Metrics.TotalCalls.Should().Be(1);
        persisted.Metrics.TotalTokenUsage.Should().Be(5);
    }

    [Fact]
    public async Task MetricsHealth_ShouldKeepRecentRetryableEventsBounded()
    {
        var tempDir = CreateTempDir();
        var faultInjector = new AiPersistenceTestFaultInjector();
        var health = new AiAuxiliaryPersistenceHealth();
        var sut = new PromptVersionManager(tempDir, faultInjector, health);
        var version = await sut.CreateVersionAsync("V1", "prompt", "baseline", "tester");
        faultInjector.SetHandler((stage, authority, _) =>
        {
            if (stage == AiPersistenceStage.JsonCommitStarted && authority == "prompt_versions")
            {
                throw new IOException("repeatable metrics commit failure");
            }
        });

        for (var index = 0; index < 40; index++)
        {
            await sut.RecordMetricsAsync(version.Id, false, index, index);
        }

        var snapshot = health.GetSnapshot();
        snapshot.Degraded.Should().BeTrue();
        snapshot.ActiveFailures.Should().ContainSingle();
        snapshot.RecentRetryableEvents.Should().HaveCount(32);
        snapshot.RecentRetryableEvents.Should().OnlyContain(item =>
            item.Authority == "prompt_versions" &&
            item.Operation == "record_metrics" &&
            item.Retryable);
    }

    [Fact]
    public async Task Restart_AroundPromptCommitInterruption_ShouldChooseCompleteOldOrNewDocument()
    {
        var tempDir = CreateTempDir();
        var beforeCommitFault = new AiPersistenceTestFaultInjector();
        var sut = new PromptVersionManager(tempDir, beforeCommitFault, new AiAuxiliaryPersistenceHealth());
        var baseline = await sut.CreateVersionAsync("V1", "prompt-1", "baseline", "tester");
        beforeCommitFault.FailOnce(
            AiPersistenceStage.JsonCommitStarted,
            static () => new AiPersistenceInterruptionException("before_prompt_commit"));

        var beforeAct = () => sut.CreateVersionAsync("V2", "prompt-2", "candidate", "tester");
        await beforeAct.Should().ThrowAsync<AiPersistenceInterruptionException>();
        var oldRestart = new PromptVersionManager(tempDir);
        (await oldRestart.ListVersionsAsync()).Should().ContainSingle(item => item.Id == baseline.Id);

        var afterCommitFault = new AiPersistenceTestFaultInjector();
        var next = new PromptVersionManager(tempDir, afterCommitFault, new AiAuxiliaryPersistenceHealth());
        afterCommitFault.FailOnce(
            AiPersistenceStage.JsonCommitCompleted,
            static () => new AiPersistenceInterruptionException("after_prompt_commit"));
        var afterAct = () => next.CreateVersionAsync("V2", "prompt-2", "committed", "tester");
        await afterAct.Should().ThrowAsync<AiPersistenceInterruptionException>();

        var newRestart = new PromptVersionManager(tempDir);
        (await newRestart.ListVersionsAsync()).Should().HaveCount(2);
        Directory.EnumerateFiles(tempDir, "prompt_versions.json.*.candidate").Should().BeEmpty();
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch
            {
            }
        }
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cv-prompt-versions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }
}
