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
        var sut = new PromptVersionManager(tempDir, faultInjector, health);
        var version = await sut.CreateVersionAsync("V1", "prompt", "baseline", "tester");
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

        var first = Task.Run(() => sut.RecordMetricsAsync(version.Id, true, 10, 20));
        firstCandidateEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        var second = Task.Run(() => sut.RecordMetricsAsync(version.Id, false, 30, 40));
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
