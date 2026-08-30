using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.AI;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public class AIGeneratedFlowVersionManagerTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    [Fact]
    public async Task SaveVersionAsync_ShouldIncrementVersionAndReturnDescendingHistory()
    {
        var tempDir = CreateTempDir();
        var sut = new AIGeneratedFlowVersionManager(tempDir);
        var flow = new OperatorFlow("AI Flow");
        var prompt = new PromptVersionInfo { VersionId = Guid.NewGuid(), Name = "Prompt V1" };
        var telemetry = new WorkflowTelemetry { TotalTimeMs = 120, LLMTokenUsage = 32 };

        var version1 = await sut.SaveVersionAsync(flow, "req-1", prompt, "OpenAI", telemetry, "tester");
        var version2 = await sut.SaveVersionAsync(flow, "req-2", prompt, "OpenAI", telemetry, "tester");

        version1.VersionNumber.Should().Be(1);
        version2.VersionNumber.Should().Be(2);

        var history = await sut.GetFlowHistoryAsync(flow.Id);
        history.Select(v => v.VersionNumber).Should().Equal(2, 1);
    }

    [Fact]
    public async Task MarkAsDeployedAsync_ShouldKeepOnlyLatestDeployedVersion()
    {
        var tempDir = CreateTempDir();
        var sut = new AIGeneratedFlowVersionManager(tempDir);
        var flow = new OperatorFlow("AI Flow");
        var prompt = new PromptVersionInfo { VersionId = Guid.NewGuid(), Name = "Prompt V1" };
        var telemetry = new WorkflowTelemetry { TotalTimeMs = 80, LLMTokenUsage = 16 };

        var version1 = await sut.SaveVersionAsync(flow, "req-1", prompt, "OpenAI", telemetry, "tester");
        var version2 = await sut.SaveVersionAsync(flow, "req-2", prompt, "OpenAI", telemetry, "tester");

        await sut.MarkAsDeployedAsync(version1.Id);
        await sut.MarkAsDeployedAsync(version2.Id);

        var history = await sut.GetFlowHistoryAsync(flow.Id);
        history.Should().ContainSingle(v => v.Id == version2.Id && v.IsDeployed);
        history.Should().ContainSingle(v => v.Id == version1.Id && !v.IsDeployed);
    }

    [Fact]
    public async Task SaveScenarioArtifactVersionAsync_ShouldKeepOnlyLatestActivePerArtifact()
    {
        var tempDir = CreateTempDir();
        var sut = new AIGeneratedFlowVersionManager(tempDir);
        const string scenarioKey = "wire-sequence-terminal";

        var modelV1 = await sut.SaveScenarioArtifactVersionAsync(
            scenarioKey,
            ScenarioArtifactType.Model,
            "wire-seq-yolo",
            "1.0.0",
            "models/wire-seq-yolo-v1.onnx");

        var modelV2 = await sut.SaveScenarioArtifactVersionAsync(
            scenarioKey,
            ScenarioArtifactType.Model,
            "wire-seq-yolo",
            "1.1.0",
            "models/wire-seq-yolo-v1.1.onnx");

        var history = await sut.GetScenarioArtifactHistoryAsync(scenarioKey, ScenarioArtifactType.Model);

        history.Should().HaveCount(2);
        history.Should().ContainSingle(item => item.Id == modelV2.Id && item.IsActive);
        history.Should().ContainSingle(item => item.Id == modelV1.Id && !item.IsActive);
    }

    [Fact]
    public async Task BuildScenarioManifestAsync_ShouldUseCurrentActiveArtifactsAndConstraints()
    {
        var tempDir = CreateTempDir();
        var sut = new AIGeneratedFlowVersionManager(tempDir);
        const string scenarioKey = "wire-sequence-terminal";

        await sut.SaveScenarioArtifactVersionAsync(
            scenarioKey,
            ScenarioArtifactType.Template,
            "terminal-wire-sequence-template",
            "1.0.0",
            "template/terminal-wire-sequence.flow.template.json",
            metadata: new Dictionary<string, string>
            {
                ["requiredResources"] = "DeepLearning.ModelPath"
            });

        var modelV1 = await sut.SaveScenarioArtifactVersionAsync(
            scenarioKey,
            ScenarioArtifactType.Model,
            "wire-seq-yolo",
            "1.0.0",
            "models/wire-seq-yolo-v1.onnx");

        var modelV2 = await sut.SaveScenarioArtifactVersionAsync(
            scenarioKey,
            ScenarioArtifactType.Model,
            "wire-seq-yolo",
            "1.1.0",
            "models/wire-seq-yolo-v1.1.onnx",
            metadata: new Dictionary<string, string>
            {
                ["requiredLabels"] = "Wire_Brown,Wire_Black,Wire_Blue",
                ["expectedSequence"] = "Wire_Brown,Wire_Black,Wire_Blue",
                ["expectedDetectionCount"] = "3",
                ["judgeOperatorType"] = "DetectionSequenceJudge"
            });

        await sut.MarkScenarioArtifactActiveAsync(modelV2.Id);
        await sut.MarkScenarioArtifactActiveAsync(modelV1.Id);
        await sut.MarkScenarioArtifactActiveAsync(modelV2.Id);

        var manifest = await sut.BuildScenarioManifestAsync(
            scenarioKey,
            "Terminal Wire Sequence",
            "Wire sequence package",
            "1.0.0",
            createdBy: "tester");

        manifest.Should().NotBeNull();
        manifest!.ScenarioKey.Should().Be(scenarioKey);
        manifest.Assets.Should().ContainSingle(item =>
            item.ArtifactType == ScenarioArtifactType.Model &&
            item.ArtifactVersion == "1.1.0" &&
            item.RelativePath == "models/wire-seq-yolo-v1.1.onnx");

        manifest.Constraints.RequiredLabels.Should().Equal("Wire_Brown", "Wire_Black", "Wire_Blue");
        manifest.Constraints.ExpectedSequence.Should().Equal("Wire_Brown", "Wire_Black", "Wire_Blue");
        manifest.Constraints.ExpectedDetectionCount.Should().Be(3);
        manifest.Constraints.JudgeOperatorType.Should().Be("DetectionSequenceJudge");
        manifest.Constraints.RequiredResources.Should().Equal("DeepLearning.ModelPath");
    }

    [Fact]
    public async Task ConcurrentFlowSaves_ShouldPersistEveryVersionWithUniqueMonotonicNumber()
    {
        var tempDir = CreateTempDir();
        var managers = new[]
        {
            new AIGeneratedFlowVersionManager(tempDir),
            new AIGeneratedFlowVersionManager(tempDir)
        };
        var flow = new OperatorFlow("Concurrent AI Flow");
        var prompt = new PromptVersionInfo { VersionId = Guid.NewGuid(), Name = "Prompt V1" };
        var telemetry = new WorkflowTelemetry { TotalTimeMs = 5, LLMTokenUsage = 3 };

        var tasks = Enumerable.Range(1, 24)
            .Select(index => Task.Run(() => managers[index % managers.Length].SaveVersionAsync(
                flow,
                $"req-{index}",
                prompt,
                "OpenAI",
                telemetry,
                "concurrent-tester")))
            .ToArray();
        await Task.WhenAll(tasks);

        var history = await new AIGeneratedFlowVersionManager(tempDir).GetFlowHistoryAsync(flow.Id);
        history.Should().HaveCount(24);
        history.Select(item => item.VersionNumber).Should().OnlyHaveUniqueItems();
        history.Select(item => item.VersionNumber).OrderBy(item => item).Should().Equal(Enumerable.Range(1, 24));
        history.Select(item => item.UserRequirement).Should().BeEquivalentTo(
            Enumerable.Range(1, 24).Select(index => $"req-{index}"));
    }

    [Fact]
    public async Task ConcurrentScenarioSaves_ShouldLoseNoRecordsAndKeepExactlyOneActiveArtifact()
    {
        var tempDir = CreateTempDir();
        var managers = new[]
        {
            new AIGeneratedFlowVersionManager(tempDir),
            new AIGeneratedFlowVersionManager(tempDir)
        };
        const string scenarioKey = "concurrent-scenario";

        var tasks = Enumerable.Range(1, 16)
            .Select(index => Task.Run(() => managers[index % managers.Length].SaveScenarioArtifactVersionAsync(
                scenarioKey,
                ScenarioArtifactType.Model,
                "shared-model",
                $"1.0.{index}",
                $"models/shared-{index}.onnx")))
            .ToArray();
        await Task.WhenAll(tasks);

        var history = await new AIGeneratedFlowVersionManager(tempDir)
            .GetScenarioArtifactHistoryAsync(scenarioKey, ScenarioArtifactType.Model);
        history.Should().HaveCount(16);
        history.Select(item => item.Id).Should().OnlyHaveUniqueItems();
        history.Should().ContainSingle(item => item.IsActive);
    }

    [Fact]
    public async Task ScenarioActivationBlockedAtCandidate_ShouldNotAllowOlderSnapshotToOverwriteLaterSave()
    {
        var tempDir = CreateTempDir();
        var faultInjector = new AiPersistenceTestFaultInjector();
        var activationManager = new AIGeneratedFlowVersionManager(tempDir, faultInjector);
        var saveManager = new AIGeneratedFlowVersionManager(tempDir);
        var first = await activationManager.SaveScenarioArtifactVersionAsync(
            "barrier-scenario",
            ScenarioArtifactType.Model,
            "model",
            "1.0.0",
            "models/one.onnx");
        using var candidateEntered = new ManualResetEventSlim(false);
        using var releaseCandidate = new ManualResetEventSlim(false);
        using var saveStarted = new ManualResetEventSlim(false);
        var blocked = 0;
        faultInjector.SetHandler((stage, authority, _) =>
        {
            if (stage == AiPersistenceStage.JsonCandidatePrepared &&
                authority == "ai_flow_versions" &&
                Interlocked.Exchange(ref blocked, 1) == 0)
            {
                candidateEntered.Set();
                releaseCandidate.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            }
        });

        var activate = Task.Run(() => activationManager.MarkScenarioArtifactActiveAsync(first.Id));
        candidateEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        var save = Task.Run(async () =>
        {
            saveStarted.Set();
            await saveManager.SaveScenarioArtifactVersionAsync(
                "barrier-scenario",
                ScenarioArtifactType.Model,
                "model",
                "1.1.0",
                "models/two.onnx");
        });
        saveStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        save.IsCompleted.Should().BeFalse();
        releaseCandidate.Set();
        await Task.WhenAll(activate, save);

        var history = await new AIGeneratedFlowVersionManager(tempDir)
            .GetScenarioArtifactHistoryAsync("barrier-scenario", ScenarioArtifactType.Model);
        history.Should().HaveCount(2);
        history.Should().ContainSingle(item => item.IsActive && item.ArtifactVersion == "1.1.0");
    }

    [Fact]
    public async Task FlowCandidateCommitFailure_ShouldPreserveOldDocumentAndRestartState()
    {
        var tempDir = CreateTempDir();
        var faultInjector = new AiPersistenceTestFaultInjector();
        var sut = new AIGeneratedFlowVersionManager(tempDir, faultInjector);
        var flow = new OperatorFlow("Faulted Flow");
        var prompt = new PromptVersionInfo { VersionId = Guid.NewGuid(), Name = "Prompt" };
        var telemetry = new WorkflowTelemetry();
        await sut.SaveVersionAsync(flow, "baseline", prompt, "OpenAI", telemetry);
        var filePath = Path.Combine(tempDir, "ai_flow_versions.json");
        var before = File.ReadAllText(filePath);
        faultInjector.FailOnce(
            AiPersistenceStage.JsonCommitStarted,
            static () => new IOException("flow commit failed"));

        var act = () => sut.SaveVersionAsync(flow, "must-not-commit", prompt, "OpenAI", telemetry);
        await act.Should().ThrowAsync<AiAuxiliaryPersistenceException>();

        File.ReadAllText(filePath).Should().Be(before);
        var history = await new AIGeneratedFlowVersionManager(tempDir).GetFlowHistoryAsync(flow.Id);
        history.Should().ContainSingle(item => item.UserRequirement == "baseline");
        Directory.EnumerateFiles(tempDir, "ai_flow_versions.json.*.candidate").Should().BeEmpty();
    }

    [Fact]
    public async Task Restart_AroundFlowCommitInterruption_ShouldChooseCompleteOldOrNewDocument()
    {
        var tempDir = CreateTempDir();
        var beforeCommitFault = new AiPersistenceTestFaultInjector();
        var flow = new OperatorFlow("Interrupted Flow");
        var prompt = new PromptVersionInfo { VersionId = Guid.NewGuid(), Name = "Prompt" };
        var telemetry = new WorkflowTelemetry();
        var firstManager = new AIGeneratedFlowVersionManager(tempDir, beforeCommitFault);
        var baseline = await firstManager.SaveVersionAsync(flow, "baseline", prompt, "OpenAI", telemetry);
        beforeCommitFault.FailOnce(
            AiPersistenceStage.JsonCommitStarted,
            static () => new AiPersistenceInterruptionException("before_flow_commit"));

        var beforeAct = () => firstManager.SaveVersionAsync(flow, "candidate", prompt, "OpenAI", telemetry);
        await beforeAct.Should().ThrowAsync<AiPersistenceInterruptionException>();
        var oldRestart = new AIGeneratedFlowVersionManager(tempDir);
        (await oldRestart.GetFlowHistoryAsync(flow.Id)).Should().ContainSingle(item => item.Id == baseline.Id);

        var afterCommitFault = new AiPersistenceTestFaultInjector();
        var secondManager = new AIGeneratedFlowVersionManager(tempDir, afterCommitFault);
        afterCommitFault.FailOnce(
            AiPersistenceStage.JsonCommitCompleted,
            static () => new AiPersistenceInterruptionException("after_flow_commit"));
        var afterAct = () => secondManager.SaveVersionAsync(flow, "committed", prompt, "OpenAI", telemetry);
        await afterAct.Should().ThrowAsync<AiPersistenceInterruptionException>();

        var newRestart = new AIGeneratedFlowVersionManager(tempDir);
        var history = await newRestart.GetFlowHistoryAsync(flow.Id);
        history.Should().HaveCount(2);
        history.Select(item => item.VersionNumber).Should().Equal(2, 1);
        history.Should().ContainSingle(item => item.Id == baseline.Id);
        history.Should().ContainSingle(item => item.UserRequirement == "committed");
        Directory.EnumerateFiles(tempDir, "ai_flow_versions.json.*.candidate").Should().BeEmpty();
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
        var dir = Path.Combine(Path.GetTempPath(), $"cv-flow-versions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }
}
