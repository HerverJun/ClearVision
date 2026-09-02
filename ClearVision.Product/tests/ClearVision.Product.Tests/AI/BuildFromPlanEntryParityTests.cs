using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Connectors;
using ClearVision.Product.Infrastructure.AI.DryRun;
using ClearVision.Product.Infrastructure.AI.Runtime;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class BuildFromPlanEntryParityTests : IDisposable
{
    private static readonly string TestOwnerHash = ConversationTestCompatibilityExtensions.OwnerHash;

    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "clearvision-build-entry-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AgentRunWebMessageAndInternalEntries_ShouldProduceEquivalentTerminalProjection()
    {
        using var harness = CreateHarness();
        var plan = BuildPlan();

        var agentRun = await RunAgentRunEntryAsync(harness, BuildRequest(plan) with { SessionId = "session-agent" });
        var webMessage = await RunWebMessageEntryAsync(harness, BuildRequest(plan) with { SessionId = "session-web" });
        var internalEntry = await RunInternalEntryAsync(harness, BuildRequest(plan) with { SessionId = "session-internal" });

        agentRun.RunId.Should().StartWith("ar_");
        webMessage.RunId.Should().StartWith("ar_");
        internalEntry.RunId.Should().StartWith("ar_");
        agentRun.Terminal.Should().NotBeNull();
        webMessage.Terminal.Should().NotBeNull();
        internalEntry.Terminal.Should().NotBeNull();
        agentRun.Replay.Events.Should().NotBeEmpty();
        webMessage.Replay.Events.Should().NotBeEmpty();
        internalEntry.Replay.Events.Should().NotBeEmpty();

        TerminalBusinessProjection(webMessage.Replay).Should().BeEquivalentTo(TerminalBusinessProjection(agentRun.Replay));
        TerminalBusinessProjection(internalEntry.Replay).Should().BeEquivalentTo(TerminalBusinessProjection(agentRun.Replay));

        harness.Conversation.GetSession("session-agent")!.History.Should().HaveCount(1);
        harness.Conversation.GetSession("session-web")!.History.Should().HaveCount(1);
        harness.Conversation.GetSession("session-internal")!.History.Should().HaveCount(1);
        harness.Projector.ProjectRecovered(agentRun.Replay).Should().BeFalse();
        harness.Projector.ProjectRecovered(webMessage.Replay).Should().BeFalse();
        harness.Projector.ProjectRecovered(internalEntry.Replay).Should().BeFalse();
    }

    [Fact]
    public async Task DisabledBuild_ShouldFailWithCanonicalCodeThroughRunService()
    {
        using var harness = CreateHarness(enabled: false);
        var entry = await RunAgentRunEntryAsync(harness, BuildRequest(BuildPlan()) with { SessionId = "session-disabled" });

        var projection = TerminalBusinessProjection(entry.Replay);
        projection.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusFailed);
        projection.FailureType.Should().Be(AiFlowGenerationResult.FailureTypeSystemError);
        projection.FailureCode.Should().Be(VisionAgentBuildFailureCodes.Disabled);
        harness.Conversation.GetSession("session-disabled")!.History.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("missing_contract", VisionAgentBuildFailureCodes.ContractInvalid)]
    [InlineData("plan_id_mismatch", VisionAgentBuildFailureCodes.PlanIdMismatch)]
    [InlineData("plan_hash_stale", VisionAgentBuildFailureCodes.StalePlan)]
    public async Task ContractFailures_ShouldFailClosedThroughRunService(string scenario, string expectedCode)
    {
        using var harness = CreateHarness();
        var plan = BuildPlan();
        var request = scenario switch
        {
            "missing_contract" => new AiFlowGenerationRequest("detect scratches")
            {
                OwnerHash = TestOwnerHash,
                SessionId = $"session-{scenario}",
                UseVisionAgentGenerateFlow = true,
                AgentGenerateFlowMode = AiAgentGenerateFlowModes.Scripted
            },
            "plan_id_mismatch" => BuildRequest(plan, build => build with { PlanId = "different-plan" }) with
            {
                SessionId = $"session-{scenario}"
            },
            _ => BuildRequest(plan, build => build with { PlanHash = "sha256:stale" }) with
            {
                SessionId = $"session-{scenario}"
            }
        };

        var entry = await RunAgentRunEntryAsync(harness, request);

        var projection = TerminalBusinessProjection(entry.Replay);
        projection.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusFailed);
        projection.FailureCode.Should().Be(expectedCode);
        harness.Conversation.GetSession($"session-{scenario}")!.History.Should().HaveCount(1);
    }

    [Fact]
    public async Task MissingWorkspaceRevision_ShouldFailThroughAgentRunWebMessageAndInternalEntries()
    {
        using var harness = CreateHarness();
        var plan = BuildPlan();
        harness.Conversation.UpdateWorkspaceSnapshot("session-missing-revision", new VisionAgentWorkspaceSnapshotUpdate
        {
            LifecycleState = "plan_ready",
            PendingPlanSnapshot = plan
        });
        var request = BuildRequest(plan) with { SessionId = "session-missing-revision" };

        var agentRun = await RunAgentRunEntryAsync(harness, request);
        var webMessage = await RunWebMessageEntryAsync(harness, request);
        var internalEntry = await RunInternalEntryAsync(harness, request);

        foreach (var entry in new[] { agentRun, webMessage, internalEntry })
        {
            var projection = TerminalBusinessProjection(entry.Replay);
            projection.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusFailed);
            projection.FailureCode.Should().Be("workspace_revision_required");
            entry.Replay.Events.Should().Contain(evt =>
                string.Equals(evt.EventType, AgentRunEventTypes.RunFailed, StringComparison.OrdinalIgnoreCase));
        }

        harness.Conversation.GetSession("session-missing-revision")!.History.Should().BeEmpty();
    }

    [Fact]
    public async Task LegacyContractWithoutHash_ShouldSucceedWithExplicitWarningThroughRunService()
    {
        using var harness = CreateHarness();
        var legacy = BuildPlan(VisionAgentPlanContractVersions.V1, includeHash: false);
        var request = BuildRequest(legacy, build => build with { PlanHash = string.Empty }) with
        {
            SessionId = "session-legacy"
        };

        var entry = await RunAgentRunEntryAsync(harness, request);

        var projection = TerminalBusinessProjection(entry.Replay);
        projection.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusCompleted);
        projection.PlanHash.Should().StartWith("sha256:");
        projection.BuildResultJson.Should().Contain("legacy_plan_hash_missing");
    }

    [Fact]
    public async Task ToolLoopMode_ShouldBeRejectedByProductionWebMessageEntry()
    {
        using var harness = CreateHarness();
        var plan = BuildPlan();
        var request = BuildRequest(plan) with
        {
            SessionId = "session-tool-web",
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.ToolLoop
        };
        var handler = new GenerateFlowMessageHandler(
            Substitute.For<IAiFlowGenerationService>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>(),
            harness.RunService,
            harness.Stream);
        string? runId = null;

        var json = await handler.HandleAsync(
            request.Description,
            request.SessionId,
            request.ExistingFlowJson,
            request.AdditionalContext,
            request.Mode,
            request.DebugPrompt,
            $"req-web-{Guid.NewGuid():N}",
            request.Attachments,
            request.RequirementMode,
            request.TemplateSelection,
            request.BuildFromPlan,
            request.UseVisionAgentGenerateFlow,
            request.AgentGenerateFlowMode,
            request.RuntimePreviewConsent,
            ownerHash: request.OwnerHash,
            onAgentRunCreated: id => runId = id);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("failureSummary").GetString()
            .Should().Contain(AiAgentGenerateFlowModePolicy.ToolLoopUnavailableCode);
        runId.Should().BeNull();
    }

    [Fact]
    public async Task ProjectedTerminal_ShouldNotDuplicateAfterJournalReloadAndReplay()
    {
        using var harness = CreateHarness();
        var entry = await RunAgentRunEntryAsync(harness, BuildRequest(BuildPlan()) with { SessionId = "session-restart" });
        harness.Conversation.GetSession("session-restart")!.History.Should().HaveCount(1);

        var reloadedJournal = new VisionAgentBuildProjectionJournal(harness.Store, harness.Redactor);
        var reloadedProjector = new VisionAgentBuildTerminalProjector(
            harness.Conversation,
            reloadedJournal,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildTerminalProjector>>());

        reloadedProjector.ProjectRecovered(entry.Replay).Should().BeFalse();
        harness.Conversation.GetSession("session-restart")!.History.Should().HaveCount(1);
    }

    [Fact]
    public async Task StartupReconciliation_ShouldProjectUnfinishedTerminalOnce()
    {
        using var harness = CreateHarness();
        var run = CreateRun(harness, "startup recovery", new
        {
            runKind = VisionAgentRunKindResolver.Build,
            sessionId = "session-startup",
            metadataOnly = true
        });
        var request = BuildRequest(BuildPlan()) with
        {
            AgentRunId = run.RunId,
            SessionId = "session-startup"
        };
        request = EnsureBuildSession(harness, request);
        var association = harness.RunService.PrepareBuildAssociation(
            BuildCommand.FromGenerationRequest(
                request,
                run.RunId,
                $"req-{run.RunId}",
                BuildCommandTransports.AgentRun,
                persistResult: false));
        association.Success.Should().BeTrue();
        var associationRevision = association.Revision;
        var submittedFingerprint = association.Snapshot!.SubmittedBuildFingerprint;
        var result = SuccessResult(request);
        harness.Stream.Complete(run.RunId, "done", new
        {
            runKind = VisionAgentRunKindResolver.Build,
            associationWorkspaceRevision = associationRevision,
            submittedBuildFingerprint = submittedFingerprint,
            planId = request.BuildFromPlan!.PlanId,
            planHash = request.BuildFromPlan.PlanHash,
            answerSetFingerprint = result.BuildResult!.AnswerSetFingerprint,
            buildIdentity = $"{request.BuildFromPlan.PlanId}:{request.BuildFromPlan.PlanHash}:{result.BuildResult.AnswerSetFingerprint}:{submittedFingerprint}",
            status = result.CompletionStatus,
            sessionId = request.SessionId,
            flow = result.Flow,
            buildResult = result.BuildResult,
            buildReadiness = result.BuildReadiness,
            metadataOnly = true
        }).Should().NotBeNull();
        harness.Conversation.GetSession("session-startup")!.History.Should().BeEmpty();

        await RunFullRecoveryAsync(harness);
        harness.Conversation.GetSession("session-startup")!.History.Should().HaveCount(1);
        var checkpoint = harness.Journal.LoadCheckpoints().Single(item => item.RunId == run.RunId);
        checkpoint.Status.Should().Be(VisionAgentBuildProjectionStatuses.Projected);
        checkpoint.TerminalMutationId.Should().StartWith("build-terminal:build:");
        checkpoint.PayloadFingerprint.Should().StartWith("sha256:");
        checkpoint.Identity.Should().NotBeNullOrWhiteSpace();
        checkpoint.ExpectedWorkspaceRevision.Should().Be(associationRevision);
        await RunFullRecoveryAsync(harness);

        harness.Conversation.GetSession("session-startup")!.History.Should().HaveCount(1);
        harness.Journal.LoadCheckpoints()
            .Single(item => item.RunId == run.RunId)
            .Status.Should()
            .Be(VisionAgentBuildProjectionStatuses.Projected);
    }

    [Fact]
    public async Task StartupReconciliation_ShouldNotProjectPlanTerminalAsBuild()
    {
        using var harness = CreateHarness();
        var run = CreateRun(harness, "plan terminal", new
        {
            runKind = VisionAgentRunKindResolver.Plan,
            mode = "plan",
            sessionId = "session-plan-terminal",
            metadataOnly = true
        });
        harness.Stream.Append(run.RunId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.PlanCompleted,
            Stage = "plan",
            Title = "Plan ready",
            Summary = "Plan completed.",
            Status = AgentRunEventStatuses.Completed,
            Payload = new
            {
                sessionId = "session-plan-terminal",
                planResult = BuildPlan(),
                metadataOnly = true
            }
        });
        harness.Stream.Complete(run.RunId, "plan done", new
        {
            runKind = VisionAgentRunKindResolver.Plan,
            mode = "plan",
            sessionId = "session-plan-terminal",
            metadataOnly = true
        }).Should().NotBeNull();

        await CreateBuildReconciler(harness).ReconcileAsync(CancellationToken.None);

        harness.Conversation.GetSession("session-plan-terminal").Should().BeNull();
        harness.Journal.LoadCheckpoints().Should().BeEmpty();
    }

    [Fact]
    public async Task StartupReconciliation_BuildTerminalMissingBasis_ShouldWriteConflictOnceWithoutProjection()
    {
        using var harness = CreateHarness();
        var run = CreateRun(harness, "missing basis", new
        {
            runKind = VisionAgentRunKindResolver.Build,
            sessionId = "session-missing-basis",
            metadataOnly = true
        });
        var request = BuildRequest(BuildPlan()) with
        {
            AgentRunId = run.RunId,
            SessionId = "session-missing-basis"
        };
        request = EnsureBuildSession(harness, request);
        harness.RunService.PrepareBuildAssociation(
            BuildCommand.FromGenerationRequest(
                request,
                run.RunId,
                $"req-{run.RunId}",
                BuildCommandTransports.AgentRun,
                persistResult: false)).Success.Should().BeTrue();
        var result = SuccessResult(request);
        harness.Stream.Complete(run.RunId, "done", new
        {
            runKind = VisionAgentRunKindResolver.Build,
            projectionDisposition = VisionAgentBuildProjectionDispositionResolver.Project,
            associationCommitted = true,
            status = result.CompletionStatus,
            sessionId = request.SessionId,
            flow = result.Flow,
            buildResult = result.BuildResult,
            buildReadiness = result.BuildReadiness,
            metadataOnly = true
        }).Should().NotBeNull();

        await RunFullRecoveryAsync(harness);
        var session = harness.Conversation.GetSession("session-missing-basis")!;
        var revision = session.WorkspaceSnapshot!.Revision;

        session.History.Should().BeEmpty();
        session.WorkspaceSnapshot.LifecycleState.Should().Be("recovery_conflict");
        harness.Journal.LoadCheckpoints().Should().BeEmpty();

        await RunFullRecoveryAsync(harness);

        harness.Conversation.GetSession("session-missing-basis")!.WorkspaceSnapshot!.Revision.Should().Be(revision);
        harness.Conversation.GetSession("session-missing-basis")!.History.Should().BeEmpty();
    }

    [Fact]
    public async Task StartupReconciliation_AssociationFailure_ShouldSkipWithoutCheckpointHistoryOrConflict()
    {
        using var harness = CreateHarness();
        var run = CreateRun(harness, "association failure", new
        {
            runKind = VisionAgentRunKindResolver.Build,
            sessionId = "session-association-failure",
            metadataOnly = true
        });
        var request = BuildRequest(BuildPlan()) with
        {
            AgentRunId = run.RunId,
            SessionId = "session-association-failure",
            BuildFromPlan = BuildRequest(BuildPlan()).BuildFromPlan! with
            {
                WorkspaceExpectedRevision = 99
            }
        };

        var result = await harness.RunService.RunAsync(
            BuildCommand.FromGenerationRequest(
                request,
                run.RunId,
                $"req-{run.RunId}",
                BuildCommandTransports.AgentRun,
                persistResult: false),
            CancellationToken.None);

        result.TerminalEvent.Should().NotBeNull();
        await RunFullRecoveryAsync(harness);

        harness.Journal.LoadCheckpoints().Should().BeEmpty();
        var session = harness.Conversation.GetSession("session-association-failure");
        session?.History.Should().BeNullOrEmpty();
        session?.WorkspaceSnapshot?.LifecycleState.Should().NotBe("recovery_conflict");
    }

    [Fact]
    public async Task StartupReconciliation_BuildHostInterrupted_ShouldRemainBuildFailedAfterBuildRecovery()
    {
        using var harness = CreateHarness();
        var run = CreateRun(harness, "interrupted build", new
        {
            runKind = VisionAgentRunKindResolver.Build,
            sessionId = "session-build-interrupted",
            metadataOnly = true
        });
        harness.Conversation.TryUpdateWorkspaceSnapshot("session-build-interrupted", new VisionAgentWorkspaceSnapshotUpdate
        {
            ClientMutationId = $"build-start:{run.RunId}",
            LifecycleState = "building",
            BuildRunId = run.RunId,
            BuildRunStatus = AgentRunEventStatuses.Running
        }).Success.Should().BeTrue();

        await RunFullRecoveryAsync(harness);
        var session = harness.Conversation.GetSession("session-build-interrupted")!;
        var revision = session.WorkspaceSnapshot!.Revision;
        session.WorkspaceSnapshot.LifecycleState.Should().Be("build_failed");
        session.WorkspaceSnapshot.BuildRunStatus.Should().Be(AgentRunEventStatuses.Failed);
        session.History.Should().BeEmpty();
        harness.Journal.LoadCheckpoints().Should().BeEmpty();

        await CreateBuildReconciler(harness).ReconcileAsync(CancellationToken.None);

        harness.Conversation.GetSession("session-build-interrupted")!.WorkspaceSnapshot!.Revision.Should().Be(revision);
        harness.Conversation.GetSession("session-build-interrupted")!.WorkspaceSnapshot!.LifecycleState.Should().Be("build_failed");
        harness.Journal.LoadCheckpoints().Should().BeEmpty();
    }

    [Theory]
    [InlineData(AgentRunEventStatuses.Failed)]
    [InlineData(AgentRunEventStatuses.Cancelled)]
    public async Task StartupReconciliation_AssociatedFailedOrCancelledBuild_ShouldProjectOnlyOnce(string terminalStatus)
    {
        using var harness = CreateHarness();
        var run = CreateRun(harness, "associated terminal", new
        {
            runKind = VisionAgentRunKindResolver.Build,
            sessionId = $"session-associated-{terminalStatus}",
            metadataOnly = true
        });
        var request = BuildRequest(BuildPlan()) with
        {
            AgentRunId = run.RunId,
            SessionId = $"session-associated-{terminalStatus}"
        };
        var association = PrepareAssociatedBuild(harness, request, run.RunId);
        var result = FailureResult(request, terminalStatus);
        var payload = BuildProjectedTerminalPayload(request, result, association);
        if (string.Equals(terminalStatus, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            harness.Stream.Cancel(run.RunId, "cancelled", payload).Should().NotBeNull();
        }
        else
        {
            harness.Stream.Fail(run.RunId, "failed", "retry", payload).Should().NotBeNull();
        }

        await RunFullRecoveryAsync(harness);
        await RunFullRecoveryAsync(harness);

        var session = harness.Conversation.GetSession(request.SessionId!)!;
        session.History.Should().HaveCount(1);
        session.WorkspaceSnapshot!.BuildRunStatus.Should().Be(terminalStatus);
        harness.Journal.LoadCheckpoints().Single(item => item.RunId == run.RunId)
            .Status.Should().Be(VisionAgentBuildProjectionStatuses.Projected);
    }

    [Fact]
    public async Task StartupReconciliation_LegacyPlanMode_ShouldRecoverAsInterruptedPlanAndSkipBuildProjector()
    {
        using var harness = CreateHarness();
        var run = CreateRun(harness, "legacy plan mode", new
        {
            mode = "plan",
            sessionId = "session-legacy-plan-mode",
            metadataOnly = true
        });
        harness.Conversation.TryUpdateWorkspaceSnapshot("session-legacy-plan-mode", new VisionAgentWorkspaceSnapshotUpdate
        {
            ClientMutationId = $"plan-start:{run.RunId}",
            LifecycleState = "planning",
            PlanRunId = run.RunId,
            PlanRunStatus = AgentRunEventStatuses.Running
        }).Success.Should().BeTrue();

        await RunFullRecoveryAsync(harness);

        var session = harness.Conversation.GetSession("session-legacy-plan-mode")!;
        session.WorkspaceSnapshot!.LifecycleState.Should().Be("plan_failed");
        session.WorkspaceSnapshot.PlanRunStatus.Should().Be(AgentRunEventStatuses.Failed);
        session.WorkspaceSnapshot.BuildRunId.Should().BeNull();
        harness.Journal.LoadCheckpoints().Should().BeEmpty();
    }

    [Fact]
    public async Task StartupReconciliation_FirstBusinessConflict_ShouldContinueWithLaterBuildRun()
    {
        using var harness = CreateHarness();
        var conflictRun = CreateRun(harness, "first conflict", new
        {
            runKind = VisionAgentRunKindResolver.Build,
            sessionId = "session-first-conflict",
            metadataOnly = true
        });
        var conflictRequest = BuildRequest(BuildPlan()) with
        {
            AgentRunId = conflictRun.RunId,
            SessionId = "session-first-conflict"
        };
        PrepareAssociatedBuild(harness, conflictRequest, conflictRun.RunId);
        harness.Stream.Complete(conflictRun.RunId, "done", new
        {
            runKind = VisionAgentRunKindResolver.Build,
            projectionDisposition = VisionAgentBuildProjectionDispositionResolver.Project,
            associationCommitted = true,
            status = AiFlowGenerationResult.CompletionStatusCompleted,
            sessionId = conflictRequest.SessionId,
            metadataOnly = true
        }).Should().NotBeNull();

        var goodRun = CreateRun(harness, "later good", new
        {
            runKind = VisionAgentRunKindResolver.Build,
            sessionId = "session-later-good",
            metadataOnly = true
        });
        var goodRequest = BuildRequest(BuildPlan()) with
        {
            AgentRunId = goodRun.RunId,
            SessionId = "session-later-good"
        };
        var association = PrepareAssociatedBuild(harness, goodRequest, goodRun.RunId);
        var goodResult = SuccessResult(goodRequest);
        harness.Stream.Complete(goodRun.RunId, "done", BuildProjectedTerminalPayload(goodRequest, goodResult, association))
            .Should().NotBeNull();

        await RunFullRecoveryAsync(harness);

        harness.Conversation.GetSession("session-first-conflict")!.WorkspaceSnapshot!.LifecycleState
            .Should().Be("recovery_conflict");
        harness.Conversation.GetSession("session-later-good")!.History.Should().HaveCount(1);
        harness.Journal.LoadCheckpoints().Should().ContainSingle(item => item.RunId == goodRun.RunId);
    }

    [Fact]
    public async Task StartupReconciliation_RepeatedFullRecovery_ShouldNotIncreaseEventsRevisionHistoryOrCheckpoints()
    {
        using var harness = CreateHarness();
        var run = CreateRun(harness, "full idempotence", new
        {
            runKind = VisionAgentRunKindResolver.Build,
            sessionId = "session-full-idempotence",
            metadataOnly = true
        });
        var request = BuildRequest(BuildPlan()) with
        {
            AgentRunId = run.RunId,
            SessionId = "session-full-idempotence"
        };
        var association = PrepareAssociatedBuild(harness, request, run.RunId);
        var result = SuccessResult(request);
        harness.Stream.Complete(run.RunId, "done", BuildProjectedTerminalPayload(request, result, association))
            .Should().NotBeNull();

        await RunFullRecoveryAsync(harness);
        var replay = harness.Stream.ReplayRaw(run.RunId)!;
        var session = harness.Conversation.GetSession("session-full-idempotence")!;
        var eventCount = replay.Events.Count;
        var revision = session.WorkspaceSnapshot!.Revision;
        var historyCount = session.History.Count;
        var checkpointCount = harness.Journal.LoadCheckpoints().Count;

        await RunFullRecoveryAsync(harness);

        harness.Stream.ReplayRaw(run.RunId)!.Events.Should().HaveCount(eventCount);
        harness.Conversation.GetSession("session-full-idempotence")!.WorkspaceSnapshot!.Revision.Should().Be(revision);
        harness.Conversation.GetSession("session-full-idempotence")!.History.Should().HaveCount(historyCount);
        harness.Journal.LoadCheckpoints().Should().HaveCount(checkpointCount);
    }

    [Fact]
    public async Task StartupReconciliation_PrimaryStorePersistenceFailure_ShouldThrow()
    {
        using var harness = CreateHarness();
        var run = CreateRun(harness, "primary store failure", new
        {
            runKind = VisionAgentRunKindResolver.Build,
            sessionId = "session-primary-store-fail",
            metadataOnly = true
        });
        var request = BuildRequest(BuildPlan()) with
        {
            AgentRunId = run.RunId,
            SessionId = "session-primary-store-fail"
        };
        var association = PrepareAssociatedBuild(harness, request, run.RunId);
        var result = SuccessResult(request);
        harness.Stream.Complete(run.RunId, "done", BuildProjectedTerminalPayload(request, result, association))
            .Should().NotBeNull();
        harness.Conversation.PrimaryStoreWriteFaultInjector = () => throw new IOException("primary failed");

        var act = () => RunFullRecoveryAsync(harness);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartupRecovery_ShouldCompletePreparedPlanTerminalIntentOnce()
    {
        using var harness = CreateHarness();
        var sessionId = "session-plan-recovery";
        var plan = BuildPlan();
        var run = CreateRun(harness, "plan startup recovery", new
        {
            sessionId,
            generationMode = "plan",
            metadataOnly = true
        });
        var initial = harness.Conversation.TryUpdateWorkspaceSnapshot(sessionId, new VisionAgentWorkspaceSnapshotUpdate
        {
            ClientMutationId = $"plan-start:{run.RunId}",
            LifecycleState = "planning",
            PlanRunId = run.RunId,
            PlanRunStatus = AgentRunEventStatuses.Running,
            RequirementMode = AiRequirementModes.Strict
        });
        var planCompleted = harness.Stream.Append(run.RunId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.PlanCompleted,
            Stage = "plan_ready",
            Title = "Plan ready",
            Summary = "Plan completed and ready to build.",
            Status = AgentRunEventStatuses.Completed,
            Payload = new
            {
                status = "plan_completed",
                generationMode = "plan",
                sessionId,
                planRunId = run.RunId,
                planResult = plan,
                planModeResult = plan,
                metadataOnly = true
            }
        })!;
        var terminalUpdate = new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = initial.Revision,
            ClientMutationId = $"plan-terminal:{run.RunId}:completed",
            LifecycleState = "plan_ready",
            PendingPlanSnapshot = plan,
            PlanRunId = run.RunId,
            PlanRunStatus = AgentRunEventStatuses.Completed,
            PlanTerminalSequence = planCompleted.Sequence,
            RequirementMode = AiRequirementModes.Strict,
            ConfirmedPlanAnswers = plan.ConfirmedPlanAnswers
        };
        var reservation = harness.Stream.TryReserveTerminal(run.RunId, AgentRunEventStatuses.Completed);
        reservation.Acquired.Should().BeTrue();
        harness.Stream.PrepareTerminalIntent(
            run.RunId,
            new AgentRunTerminalIntentDraft
            {
                SessionId = sessionId,
                RunType = "plan",
                TargetStatus = AgentRunEventStatuses.Completed,
                TerminalMutationId = terminalUpdate.ClientMutationId!,
                PayloadFingerprint = ConversationalFlowService.ComputeWorkspaceMutationFingerprint(terminalUpdate),
                ExpectedWorkspaceRevision = terminalUpdate.ExpectedRevision,
                Identity = $"{plan.PlanId}:{plan.PlanHash}",
                Phase = "TerminalPrepared"
            },
            reservation).Should().NotBeNull();

        var restartedStream = new AgentRunEventStreamService(harness.Store, harness.Redactor);
        var recovery = new VisionAgentRunRecoveryReconciliationService(
            harness.Store,
            restartedStream,
            harness.Conversation,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentRunRecoveryReconciliationService>>());

        await recovery.ReconcileAsync(CancellationToken.None);
        var replay = restartedStream.ReplayRaw(run.RunId)!;
        var session = harness.Conversation.GetSession(sessionId)!;
        var revision = session.WorkspaceSnapshot!.Revision;
        var eventCount = replay.Events.Count;
        var receiptCount = session.MutationReceipts.Count;

        replay.Events.Should().ContainSingle(evt => evt.EventType == AgentRunEventTypes.RunCompleted);
        session.WorkspaceSnapshot.PlanRunStatus.Should().Be(AgentRunEventStatuses.Completed);
        session.WorkspaceSnapshot.PlanTerminalSequence.Should().Be(planCompleted.Sequence);
        session.MutationReceipts.Should().Contain(receipt =>
            receipt.MutationId == terminalUpdate.ClientMutationId &&
            receipt.PayloadFingerprint == ConversationalFlowService.ComputeWorkspaceMutationFingerprint(terminalUpdate));

        await recovery.ReconcileAsync(CancellationToken.None);

        restartedStream.ReplayRaw(run.RunId)!.Events.Should().HaveCount(eventCount);
        harness.Conversation.GetSession(sessionId)!.WorkspaceSnapshot!.Revision.Should().Be(revision);
        harness.Conversation.GetSession(sessionId)!.MutationReceipts.Should().HaveCount(receiptCount);
    }

    [Fact]
    public async Task StartupRecovery_TerminalPlanWithDeletedSession_ShouldSkipWorkspaceProjection()
    {
        using var harness = CreateHarness();
        const string sessionId = "session-deleted-before-plan-recovery";
        var plan = BuildPlan();
        var run = CreateRun(harness, "deleted session plan recovery", new
        {
            sessionId,
            generationMode = "plan",
            metadataOnly = true
        });
        harness.Conversation.TryInitializeWorkspaceSnapshot(
            TestOwnerHash,
            sessionId,
            new VisionAgentWorkspaceSnapshotUpdate
            {
                ClientMutationId = $"plan-start:{run.RunId}",
                LifecycleState = "planning",
                PlanRunId = run.RunId,
                PlanRunStatus = AgentRunEventStatuses.Running,
                RequirementMode = AiRequirementModes.Strict
            }).Success.Should().BeTrue();
        harness.Stream.Append(run.RunId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.PlanCompleted,
            Stage = "plan_ready",
            Title = "Plan ready",
            Summary = "Plan completed before its conversation was deleted.",
            Status = AgentRunEventStatuses.Completed,
            Payload = new
            {
                status = "plan_completed",
                generationMode = "plan",
                sessionId,
                planRunId = run.RunId,
                planResult = plan,
                planModeResult = plan,
                metadataOnly = true
            }
        }).Should().NotBeNull();
        harness.Stream.Complete(run.RunId, "terminal before conversation deletion", new
        {
            sessionId,
            planRunId = run.RunId,
            metadataOnly = true
        }).Should().NotBeNull();
        harness.Conversation.DeleteSessionWithResult(TestOwnerHash, sessionId).Status
            .Should().Be(ConversationSessionDeleteStatus.Deleted);
        var eventCount = harness.Stream.ReplayRaw(run.RunId)!.Events.Count;
        var recovery = new VisionAgentRunRecoveryReconciliationService(
            harness.Store,
            new AgentRunEventStreamService(harness.Store, harness.Redactor),
            harness.Conversation,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentRunRecoveryReconciliationService>>());

        var act = () => recovery.ReconcileAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        harness.Conversation.GetSession(TestOwnerHash, sessionId).Should().BeNull();
        harness.Stream.ReplayRaw(run.RunId)!.Events.Should().HaveCount(eventCount);
    }

    [Fact]
    public async Task StartupRecovery_CrossOwnerSessionReference_ShouldNotMutateForeignWorkspace()
    {
        using var harness = CreateHarness();
        const string runOwnerHash = "usr_recovery_owner_a";
        const string sessionOwnerHash = "usr_recovery_owner_b";
        const string sessionId = "session-cross-owner-recovery";
        var plan = BuildPlan();
        var run = harness.Stream.CreateRun(
            "cross-owner recovery must fail closed",
            new
            {
                sessionId,
                generationMode = "plan",
                metadataOnly = true
            },
            runOwnerHash);
        var initial = harness.Conversation.TryInitializeWorkspaceSnapshot(
            sessionOwnerHash,
            sessionId,
            new VisionAgentWorkspaceSnapshotUpdate
            {
                ClientMutationId = $"foreign-plan-start:{run.RunId}",
                LifecycleState = "planning",
                PlanRunId = run.RunId,
                PlanRunStatus = AgentRunEventStatuses.Running,
                RequirementMode = AiRequirementModes.Strict
            });
        initial.Success.Should().BeTrue();
        var planCompleted = harness.Stream.Append(run.RunId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.PlanCompleted,
            Stage = "plan_ready",
            Title = "Plan ready",
            Summary = "Forged cross-owner plan terminal evidence.",
            Status = AgentRunEventStatuses.Completed,
            Payload = new
            {
                status = "plan_completed",
                generationMode = "plan",
                sessionId,
                planRunId = run.RunId,
                planResult = plan,
                planModeResult = plan,
                metadataOnly = true
            }
        });
        planCompleted.Should().NotBeNull();
        harness.Stream.Complete(run.RunId, "terminal before restart", new
        {
            sessionId,
            planRunId = run.RunId,
            metadataOnly = true
        }).Should().NotBeNull();
        var before = harness.Conversation.GetSession(sessionOwnerHash, sessionId)!;
        var beforeRevision = before.WorkspaceSnapshot!.Revision;
        var beforeReceiptCount = before.MutationReceipts.Count;
        var restartedStream = new AgentRunEventStreamService(harness.Store, harness.Redactor);
        var recovery = new VisionAgentRunRecoveryReconciliationService(
            harness.Store,
            restartedStream,
            harness.Conversation,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentRunRecoveryReconciliationService>>());

        await recovery.ReconcileAsync(CancellationToken.None);

        var after = harness.Conversation.GetSession(sessionOwnerHash, sessionId)!;
        after.WorkspaceSnapshot!.Revision.Should().Be(beforeRevision);
        after.WorkspaceSnapshot.PlanRunStatus.Should().Be(AgentRunEventStatuses.Running);
        after.WorkspaceSnapshot.PendingPlanSnapshot.Should().BeNull();
        after.MutationReceipts.Should().HaveCount(beforeReceiptCount);
        harness.Conversation.GetSession(runOwnerHash, sessionId).Should().BeNull();
    }

    [Fact]
    public void ProjectorRetry_ShouldRepairPartialProjectionWithoutDuplicatingStableAssistantTurn()
    {
        using var harness = CreateHarness();
        var run = CreateRun(harness, "partial projection", new { sessionId = "session-partial", metadataOnly = true });
        var request = BuildRequest(BuildPlan()) with
        {
            AgentRunId = run.RunId,
            SessionId = "session-partial"
        };
        var association = PrepareAssociatedBuild(harness, request, run.RunId);
        var result = SuccessResult(request);
        var terminal = harness.Stream.Complete(
            run.RunId,
            "done",
            BuildProjectedTerminalPayload(request, result, association))!;
        var partialConversation = new PartialProjectionConversationService(harness.Conversation);
        var projector = new VisionAgentBuildTerminalProjector(
            partialConversation,
            harness.Journal,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildTerminalProjector>>());

        projector.Project(new VisionAgentBuildTerminalProjection(
            run.RunId,
            BuildCommandTransports.AgentRun,
            request,
            result,
            terminal)).Should().BeFalse();
        projector.Project(new VisionAgentBuildTerminalProjection(
            run.RunId,
            BuildCommandTransports.AgentRun,
            request,
            result,
            terminal)).Should().BeTrue();

        var session = harness.Conversation.GetSession("session-partial")!;
        session.History.Should().HaveCount(1);
        session.WorkspaceSnapshot!.BuildRunId.Should().Be(run.RunId);
        session.WorkspaceSnapshot.BuildRunStatus.Should().Be(AgentRunEventStatuses.Completed);
        session.WorkspaceSnapshot.BuildTerminalSequence.Should().Be(terminal.Sequence);
        var checkpoint = harness.Journal.LoadCheckpoints().Single(item => item.RunId == run.RunId);
        checkpoint.Status.Should().Be(VisionAgentBuildProjectionStatuses.Projected);
        checkpoint.Attempts.Should().Be(2);
    }

    [Fact]
    public void ProjectorFailure_ShouldRemainRetryableAndThenProjectOnce()
    {
        using var harness = CreateHarness();
        var run = CreateRun(harness, "projection retry", new { sessionId = "session-retry", metadataOnly = true });
        var request = BuildRequest(BuildPlan()) with
        {
            SessionId = "session-retry",
            AgentRunId = run.RunId
        };
        EnsureBuildSession(harness, request);
        var result = SuccessResult(request);
        var terminal = harness.Stream.Complete(run.RunId, "done", new { status = result.CompletionStatus, sessionId = request.SessionId, flow = result.Flow, buildResult = result.BuildResult, metadataOnly = true })!;
        var throwingConversation = new ThrowOnceConversationService(harness.Conversation);
        var projector = new VisionAgentBuildTerminalProjector(
            throwingConversation,
            harness.Journal,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildTerminalProjector>>());

        projector.Project(new VisionAgentBuildTerminalProjection(run.RunId, BuildCommandTransports.AgentRun, request, result, terminal))
            .Should()
            .BeFalse();
        projector.Project(new VisionAgentBuildTerminalProjection(run.RunId, BuildCommandTransports.AgentRun, request, result, terminal))
            .Should()
            .BeTrue();

        harness.Conversation.GetSession("session-retry")!.History.Should().HaveCount(1);
        var checkpoint = harness.Journal.LoadCheckpoints().Single(item => item.RunId == run.RunId);
        checkpoint.Status.Should().Be(VisionAgentBuildProjectionStatuses.Projected);
        checkpoint.Attempts.Should().Be(2);
    }

    [Fact]
    public void SessionIdPolicy_ShouldUseDeterministicRunSessionAndIgnoreDrift()
    {
        using var harness = CreateHarness();
        var run = CreateRun(harness, "session policy", new { metadataOnly = true });
        var terminal = harness.Stream.Cancel(run.RunId)!;
        var request = BuildRequest(BuildPlan()) with
        {
            SessionId = "bad/path",
            AgentRunId = run.RunId
        };
        harness.Conversation.TryInitializeWorkspaceSnapshot(
            TestOwnerHash,
            $"agent-run-{run.RunId}",
            new VisionAgentWorkspaceSnapshotUpdate { LifecycleState = "building" }).Success.Should().BeTrue();
        var result = new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusCancelled,
            FailureType = AiFlowGenerationResult.FailureTypeUserCancelled
        };

        harness.Projector.Project(new VisionAgentBuildTerminalProjection(run.RunId, BuildCommandTransports.AgentRun, request, result, terminal))
            .Should()
            .BeTrue();
        harness.Projector.Project(new VisionAgentBuildTerminalProjection(
                run.RunId,
                BuildCommandTransports.AgentRun,
                request with { SessionId = "session-drift" },
                result,
                terminal))
            .Should()
            .BeFalse();

        harness.Conversation.GetSession($"agent-run-{run.RunId}")!.History.Should().HaveCount(1);
        harness.Conversation.GetSession("session-drift").Should().BeNull();
    }

    [Fact]
    public void Cleanup_ShouldNotDeletePendingOrFailedProjectionCheckpoints()
    {
        using var harness = CreateHarness();
        var begin = harness.Journal.Begin("ar_cleanup", "session-cleanup", 3, AgentRunEventTypes.RunCompleted);
        begin.Status.Should().Be(VisionAgentBuildProjectionBeginStatus.Started);
        harness.Journal.MarkFailed("ar_cleanup", "session-cleanup", 3, AgentRunEventTypes.RunCompleted, new InvalidOperationException("public failure"));

        harness.Journal.Cleanup(DateTimeOffset.UtcNow.AddDays(60), TimeSpan.FromDays(1));

        var checkpoint = harness.Journal.LoadCheckpoints().Single(item => item.RunId == "ar_cleanup");
        checkpoint.Status.Should().Be(VisionAgentBuildProjectionStatuses.Failed);
        checkpoint.PublicErrorMessage.Should().Be("public failure");
    }

    [Fact]
    public void JournalBegin_WhenCheckpointMetadataDrifts_ShouldReturnMetadataConflict()
    {
        using var harness = CreateHarness();
        harness.Journal.Begin(
            "ar_metadata",
            "session-metadata",
            4,
            AgentRunEventTypes.RunCompleted,
            "mutation:first",
            "sha256:first",
            10,
            "identity:first").Status.Should().Be(VisionAgentBuildProjectionBeginStatus.Started);

        var conflict = harness.Journal.Begin(
            "ar_metadata",
            "session-metadata",
            4,
            AgentRunEventTypes.RunCompleted,
            "mutation:first",
            "sha256:second",
            10,
            "identity:first");

        conflict.Status.Should().Be(VisionAgentBuildProjectionBeginStatus.MetadataConflict);
        conflict.Checkpoint!.PayloadFingerprint.Should().Be("sha256:first");
        harness.Journal.LoadCheckpoints().Single(item => item.RunId == "ar_metadata")
            .PayloadFingerprint.Should().Be("sha256:first");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private BuildHarness CreateHarness(bool enabled = true)
    {
        var directory = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        var redactor = new AgentRunEventRedactor();
        var store = new AgentRunEventStore(Path.Combine(directory, "events"), redactor);
        var stream = new AgentRunEventStreamService(store, redactor);
        var journal = new VisionAgentBuildProjectionJournal(store, redactor);
        var conversation = new ConversationalFlowService(Path.Combine(directory, "sessions"));
        var execution = new FakeBuildExecution();
        var application = new VisionAgentBuildApplicationService(
            execution,
            new VisionAgentPlanAnswerValidator(),
            new VisionAgentPlanRequirementOverlay(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildApplicationService>>(),
            Options.Create(new AgentGenerateFlowOptions
            {
                Enabled = enabled
            }));
        var projector = new VisionAgentBuildTerminalProjector(
            conversation,
            journal,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildTerminalProjector>>());
        var runService = new VisionAgentBuildRunService(
            application,
            stream,
            conversation,
            projector,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildRunService>>());

        return new BuildHarness(directory, redactor, store, stream, journal, conversation, projector, runService);
    }

    private static VisionAgentBuildProjectionReconciliationService CreateBuildReconciler(BuildHarness harness)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IVisionAgentBuildTerminalProjector>(
            new VisionAgentBuildTerminalProjector(
                harness.Conversation,
                harness.Journal,
                Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildTerminalProjector>>()));
        var provider = services.BuildServiceProvider();
        return new VisionAgentBuildProjectionReconciliationService(
            harness.Store,
            new AgentRunEventStreamService(harness.Store, harness.Redactor),
            harness.Journal,
            harness.Conversation,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new VisionAgentBuildProjectionDispositionResolver(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildProjectionReconciliationService>>());
    }

    private static async Task RunFullRecoveryAsync(BuildHarness harness)
    {
        var runRecovery = new VisionAgentRunRecoveryReconciliationService(
            harness.Store,
            new AgentRunEventStreamService(harness.Store, harness.Redactor),
            harness.Conversation,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentRunRecoveryReconciliationService>>());
        await runRecovery.ReconcileAsync(CancellationToken.None);
        await CreateBuildReconciler(harness).ReconcileAsync(CancellationToken.None);
    }

    private static AgentRunCreateResult CreateRun(
        BuildHarness harness,
        string description,
        object? payload = null) =>
        harness.Stream.CreateRun(description, payload, TestOwnerHash);

    private static AiFlowGenerationRequest EnsureBuildSession(
        BuildHarness harness,
        AiFlowGenerationRequest request)
    {
        var ownedRequest = request with { OwnerHash = TestOwnerHash };
        var sessionId = ownedRequest.SessionId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sessionId) ||
            harness.Conversation.GetSession(TestOwnerHash, sessionId) != null)
        {
            return ownedRequest;
        }

        var initial = harness.Conversation.TryInitializeWorkspaceSnapshot(
            TestOwnerHash,
            sessionId,
            new VisionAgentWorkspaceSnapshotUpdate
            {
                LifecycleState = "plan_ready",
                PendingPlanSnapshot = ownedRequest.BuildFromPlan?.PlanSnapshot,
                RequirementMode = ownedRequest.RequirementMode
            });
        initial.Success.Should().BeTrue();

        if (ownedRequest.BuildFromPlan == null ||
            ownedRequest.BuildFromPlan.WorkspaceExpectedRevision.HasValue)
        {
            return ownedRequest;
        }

        return ownedRequest with
        {
            BuildFromPlan = ownedRequest.BuildFromPlan with
            {
                WorkspaceExpectedRevision = initial.Revision
            }
        };
    }

    private static VisionAgentWorkspaceSnapshotMutationResult PrepareAssociatedBuild(
        BuildHarness harness,
        AiFlowGenerationRequest request,
        string runId)
    {
        request = EnsureBuildSession(harness, request);
        var association = harness.RunService.PrepareBuildAssociation(
            BuildCommand.FromGenerationRequest(
                request,
                runId,
                $"req-{runId}",
                BuildCommandTransports.AgentRun,
                persistResult: false));
        association.Success.Should().BeTrue();
        association.Snapshot.Should().NotBeNull();
        return association;
    }

    private static object BuildProjectedTerminalPayload(
        AiFlowGenerationRequest request,
        AiFlowGenerationResult result,
        VisionAgentWorkspaceSnapshotMutationResult association)
    {
        var build = request.BuildFromPlan!;
        var answerSetFingerprint = FirstNonBlank(
            result.AnswerSetFingerprint,
            result.BuildResult?.AnswerSetFingerprint,
            build.PlanHash,
            build.PlanSnapshot?.PlanHash);
        var submittedFingerprint = FirstNonBlank(
            association.Snapshot?.SubmittedBuildFingerprint,
            answerSetFingerprint,
            build.PlanHash,
            build.PlanSnapshot?.PlanHash);
        return new
        {
            runKind = VisionAgentRunKindResolver.Build,
            projectionDisposition = VisionAgentBuildProjectionDispositionResolver.Project,
            associationCommitted = true,
            associationWorkspaceRevision = association.Revision,
            submittedBuildFingerprint = submittedFingerprint,
            planId = build.PlanId,
            planHash = build.PlanHash,
            answerSetFingerprint,
            buildIdentity = BuildBuildIdentity(build.PlanId, build.PlanHash, answerSetFingerprint, submittedFingerprint),
            status = result.CompletionStatus,
            sessionId = request.SessionId,
            failureType = result.FailureType,
            failureCode = result.FailureSummary?.Code ?? string.Empty,
            failureSummary = result.FailureSummary,
            flow = result.Flow,
            buildResult = result.BuildResult,
            buildReadiness = result.BuildReadiness,
            planSnapshot = build.PlanSnapshot,
            buildFromPlan = new
            {
                planId = build.PlanId,
                planHash = build.PlanHash,
                planSnapshot = build.PlanSnapshot,
                metadataOnly = true
            },
            metadataOnly = true
        };
    }

    private static AiFlowGenerationResult FailureResult(AiFlowGenerationRequest request, string terminalStatus)
    {
        var cancelled = string.Equals(terminalStatus, AgentRunEventStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);
        return new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = cancelled
                ? AiFlowGenerationResult.CompletionStatusCancelled
                : AiFlowGenerationResult.CompletionStatusFailed,
            FailureType = cancelled
                ? AiFlowGenerationResult.FailureTypeUserCancelled
                : AiFlowGenerationResult.FailureTypeSystemError,
            ErrorMessage = cancelled ? "cancelled" : "failed",
            FailureSummary = new AiFailureSummary
            {
                Category = "test",
                Code = cancelled ? "test_cancelled" : "test_failed",
                Message = cancelled ? "cancelled" : "failed",
                RepairTarget = "retry"
            },
            BuildResult = new VisionAgentBuildResult
            {
                BuildId = "build-fake",
                PlanId = request.BuildFromPlan!.PlanId,
                PlanHash = request.BuildFromPlan.PlanHash,
                ContractVersion = request.BuildFromPlan.PlanSnapshot?.PlanContractVersion ?? string.Empty,
                AnswerSetFingerprint = "manual",
                MetadataOnly = true
            },
            BuildReadiness = request.BuildFromPlan.PlanSnapshot?.BuildReadiness,
            PlanId = request.BuildFromPlan.PlanId,
            PlanHash = request.BuildFromPlan.PlanHash,
            AnswerSetFingerprint = "manual",
            SessionId = request.SessionId,
            InteractionState = cancelled ? AiInteractionStates.Idle : AiInteractionStates.Failed,
            TurnIntent = AiTurnIntents.NewFlow,
            RouterConfidence = AiRouterConfidence.High
        };
    }

    private static string BuildBuildIdentity(
        string planId,
        string planHash,
        string answerSetFingerprint,
        string submittedBuildFingerprint)
    {
        return string.Join(
            ":",
            new[] { planId, planHash, answerSetFingerprint, submittedBuildFingerprint }
                .Select(SanitizeIdentityToken)
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string SanitizeIdentityToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            string.Empty,
            value.Trim().Where(ch => char.IsLetterOrDigit(ch) || ch is ':' or '_' or '-' or '.'));
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static async Task<EntryResult> RunAgentRunEntryAsync(
        BuildHarness harness,
        AiFlowGenerationRequest request)
    {
        request = EnsureBuildSession(harness, request);
        var create = CreateRun(harness, request.Description, new
        {
            runKind = VisionAgentRunKindResolver.Build,
            sessionId = request.SessionId,
            planId = request.BuildFromPlan?.PlanId ?? string.Empty,
            planHash = request.BuildFromPlan?.PlanHash ?? request.BuildFromPlan?.PlanSnapshot?.PlanHash ?? string.Empty,
            metadataOnly = true
        });
        var runRequest = request with { AgentRunId = create.RunId };
        var result = await harness.RunService.RunAsync(
            BuildCommand.FromGenerationRequest(
                runRequest,
                create.RunId,
                $"req-{create.RunId}",
                BuildCommandTransports.AgentRun,
                persistResult: false),
            CancellationToken.None);
        var replay = harness.Stream.Replay(create.RunId)!;
        return new EntryResult(create.RunId, result.TerminalEvent, replay);
    }

    private static async Task<EntryResult> RunWebMessageEntryAsync(
        BuildHarness harness,
        AiFlowGenerationRequest request)
    {
        request = EnsureBuildSession(harness, request);
        var handler = new GenerateFlowMessageHandler(
            Substitute.For<IAiFlowGenerationService>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>(),
            harness.RunService,
            harness.Stream);
        string? runId = null;
        var json = await handler.HandleAsync(
            request.Description,
            request.SessionId,
            request.ExistingFlowJson,
            request.AdditionalContext,
            request.Mode,
            request.DebugPrompt,
            $"req-web-{Guid.NewGuid():N}",
            request.Attachments,
            request.RequirementMode,
            request.TemplateSelection,
            request.BuildFromPlan,
            request.UseVisionAgentGenerateFlow,
            request.AgentGenerateFlowMode,
            request.RuntimePreviewConsent,
            ownerHash: request.OwnerHash,
            onAgentRunCreated: id => runId = id);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("completionStatus").GetString()
            .Should()
            .NotBeNullOrWhiteSpace();
        runId.Should().NotBeNullOrWhiteSpace();
        var replay = harness.Stream.Replay(runId!)!;
        return new EntryResult(runId!, Terminal(replay), replay);
    }

    private static async Task<EntryResult> RunInternalEntryAsync(
        BuildHarness harness,
        AiFlowGenerationRequest request)
    {
        request = EnsureBuildSession(harness, request);
        var service = CreateInternalGenerationService(harness);
        var result = await service.GenerateFlowAsync(request, cancellationToken: CancellationToken.None);
        result.SessionId.Should().Be(request.SessionId);
        result.CompletionStatus.Should().NotBeNullOrWhiteSpace();
        result.PlanId.Should().NotBeNullOrWhiteSpace();

        var runId = harness.Stream.ReplayLatest(TestOwnerHash)?.Summary.RunId;
        runId.Should().NotBeNullOrWhiteSpace();
        var replay = harness.Stream.Replay(runId!)!;
        return new EntryResult(runId!, Terminal(replay), replay);
    }

    private static AiFlowGenerationService CreateInternalGenerationService(BuildHarness harness)
    {
        var operatorFactory = Substitute.For<IOperatorFactory>();
        var flowExecutionService = Substitute.For<IFlowExecutionService>();
        var promptVersionManager = Substitute.For<IPromptVersionManager>();
        promptVersionManager.GetActiveVersionAsync().Returns(Task.FromResult(new PromptVersion
        {
            Id = Guid.NewGuid(),
            Name = "Test Prompt",
            Content = "test"
        }));

        return new AiFlowGenerationService(
            new AiGenerationOrchestrator(
                Substitute.For<IAiModelSelector>(),
                Substitute.For<IAiConnectorFactory>()),
            new PromptBuilder(operatorFactory),
            harness.Conversation,
            Substitute.For<IAiFlowValidator>(),
            new AutoLayoutService(),
            operatorFactory,
            Substitute.For<IFlowTemplateService>(),
            Substitute.For<IScenarioMatcher>(),
            Substitute.For<IRequirementBriefExtractor>(),
            Substitute.For<IAiTurnRouter>(),
            Substitute.For<ITemplateConstraintValidator>(),
            new AiFlowResponseParser(),
            new DryRunService(flowExecutionService),
            Substitute.For<IHostEnvironment>(),
            promptVersionManager,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<AiFlowGenerationService>>(),
            Options.Create(new AgentGenerateFlowOptions
            {
                Enabled = true
            }),
            harness.RunService,
            harness.Stream);
    }

    private static TerminalBusinessProjectionSnapshot TerminalBusinessProjection(AgentRunReplayResult replay)
    {
        var terminal = Terminal(replay);
        var source = TerminalSource(terminal);
        return new TerminalBusinessProjectionSnapshot(
            ReadString(source, "status"),
            ReadString(source, "failureType"),
            ReadString(source, "failureCode"),
            ReadString(source, "planId"),
            ReadString(source, "planHash"),
            ReadString(source, "contractVersion"),
            ReadString(source, "answerSetFingerprint"),
            ReadString(source, "requestedMode"),
            ReadString(source, "effectiveMode"),
            ReadBool(source, "toolLoopEntered"),
            ReadString(source, "fallbackReason"),
            ReadRawJson(source, "buildReadiness"),
            ReadRawJson(source, "flow"),
            ReadRawJson(source, "buildResult"),
            ReadRawJson(source, "workflowDiff"),
            ReadRawJson(source, "applyGate"),
            ReadRawJson(source, "pendingParameters"),
            ReadRawJson(source, "missingResources"),
            ReadString(source, "firstFixRecommendation"));
    }

    private static AgentRunEvent Terminal(AgentRunReplayResult replay)
    {
        return replay.Events.Last(evt =>
            evt.EventType is AgentRunEventTypes.RunCompleted or
                AgentRunEventTypes.RunFailed or
                AgentRunEventTypes.RunCancelled);
    }

    private static JsonElement TerminalSource(AgentRunEvent terminal)
    {
        var payload = JsonSerializer.SerializeToElement(terminal.Payload, AgentRunEventJson.Options);
        if (terminal.EventType == AgentRunEventTypes.RunFailed &&
            TryGetProperty(payload, "diagnostic", out var diagnostic))
        {
            return diagnostic;
        }

        return payload;
    }

    private sealed record TerminalBusinessProjectionSnapshot(
        string? CompletionStatus,
        string? FailureType,
        string? FailureCode,
        string? PlanId,
        string? PlanHash,
        string? ContractVersion,
        string? AnswerFingerprint,
        string? RequestedMode,
        string? EffectiveMode,
        bool ToolLoopEntered,
        string? FallbackReason,
        string BuildReadinessJson,
        string FlowJson,
        string BuildResultJson,
        string WorkflowDiffJson,
        string ApplyGateJson,
        string PendingParametersJson,
        string MissingResourcesJson,
        string? FirstFix);

    private static string ReadRawJson(JsonElement source, string name)
    {
        return TryGetProperty(source, name, out var property)
            ? property.GetRawText()
            : string.Empty;
    }

    private static string ReadString(JsonElement source, string name)
    {
        if (!TryGetProperty(source, name, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
    }

    private static bool ReadBool(JsonElement source, string name)
    {
        if (!TryGetProperty(source, name, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(property.GetString(), out var parsed) && parsed,
            _ => false
        };
    }

    private static bool TryGetProperty(JsonElement source, string name, out JsonElement property)
    {
        if (source.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in source.EnumerateObject())
            {
                if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    property = item.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }

    private static AiFlowGenerationRequest BuildRequest(
        VisionAgentPlanModeResult plan,
        Func<VisionAgentBuildFromPlanRequest, VisionAgentBuildFromPlanRequest>? mutate = null)
    {
        var build = new VisionAgentBuildFromPlanRequest
        {
            PlanId = plan.PlanId,
            PlanHash = plan.PlanHash,
            PlanSnapshot = plan,
            ConfirmedAnswers = plan.ConfirmedPlanAnswers,
            AcceptedRecommendedDefaults = true,
            ResourceDecisions = [BoundCameraBindingDecision()],
            OperatorCatalogVersion = plan.OperatorCatalogVersion,
            StationBoundarySummary = plan.StationBoundarySummary,
            PlcOutputPolicy = plan.PlcOutputPolicy,
            BuildIntent = "new",
            OriginalUserPrompt = plan.OriginalUserPrompt
        };
        build = mutate?.Invoke(build) ?? build;
        return new AiFlowGenerationRequest("detect scratches", Mode: GenerateFlowMode.New)
        {
            OwnerHash = TestOwnerHash,
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Scripted,
            BuildFromPlan = build
        };
    }

    private static VisionAgentPlanModeResult BuildPlan(
        string version = VisionAgentPlanContractVersions.V2,
        bool includeHash = true)
    {
        var answers = new List<VisionAgentPlanAnswer>
        {
            Answer(VisionAgentPlanAnswerFields.InspectionObject, "metal panel"),
            Answer(VisionAgentPlanAnswerFields.TaskType, AiVisionTaskTypes.SurfaceDefect),
            Answer(VisionAgentPlanAnswerFields.ImageSource, "camera"),
            Answer(VisionAgentPlanAnswerFields.AcceptanceCriteria, "no visible scratch"),
            Answer(VisionAgentPlanAnswerFields.OutputTarget, "ok_ng")
        };
        var plan = new VisionAgentPlanModeResult
        {
            PlanContractVersion = version,
            PlanId = "plan-entry-parity",
            OriginalUserPrompt = "detect scratches on metal panel",
            Goal = "detect scratches on metal panel",
            Intent = AiVisionTaskTypes.SurfaceDefect,
            Confidence = "high",
            RequirementUnderstanding = ["metal panel", "scratch", "ok/ng"],
            ConfirmedPlanAnswers = answers,
            ResolvedPlanFields = answers.Select(answer => answer.Field).ToList(),
            RemainingPlanFields = [],
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = "surface_defect_detection",
                Title = "Surface defect detection",
                Summary = "Detect scratches and output OK/NG.",
                Operators = ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"],
                TemplateDecision = "free_generate"
            },
            CanBuild = true,
            BuildReadiness = new VisionAgentBuildReadinessSnapshot
            {
                CanBuild = true,
                ResolvedFields = answers.Select(answer => answer.Field).ToList(),
                ContractVersion = version
            },
            RequirementMaturity = new AiRequirementMaturityResult
            {
                Maturity = AiRequirementMaturity.Actionable,
                TaskType = AiVisionTaskTypes.SurfaceDefect,
                CanPlan = true,
                CanBuild = true,
                ObjectSignals = ["metal panel"],
                TaskSignals = ["scratch"],
                PublicReason = "ready"
            },
            OperatorCatalogVersion = "catalog-test",
            StationBoundarySummary = "metadata only",
            PlcOutputPolicy = "ok_ng",
            MetadataOnly = true
        };

        return includeHash
            ? plan with { PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan) }
            : plan;
    }

    private static VisionAgentPlanAnswer Answer(string field, string value)
    {
        return new VisionAgentPlanAnswer
        {
            QuestionId = $"q_{field}",
            Field = field,
            Value = value,
            Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection,
            Resolved = true
        };
    }

    private static VisionAgentResourceDecision BoundCameraBindingDecision()
    {
        var operatorKey = VisionAgentResourceIdentity.OperatorKey("ImageAcquisition", 0);
        return new VisionAgentResourceDecision
        {
            CanonicalId = VisionAgentResourceIdentity.CreateCanonicalId(
                "camera_binding",
                operatorKey,
                "CameraBindingId"),
            Status = VisionAgentResourceStatuses.Bound,
            ResourceKey = $"{operatorKey}.CameraBindingId",
            ResourceType = "camera_binding",
            OperatorKey = operatorKey,
            OperatorType = "ImageAcquisition",
            OperatorIndex = 0,
            ParameterName = "CameraBindingId",
            Source = "resource_binding"
        };
    }

    private static AiFlowGenerationResult SuccessResult(AiFlowGenerationRequest request)
    {
        var build = request.BuildFromPlan!;
        var plan = build.PlanSnapshot!;
        var buildResult = new VisionAgentBuildResult
        {
            BuildId = "build-fake",
            PlanId = build.PlanId,
            PlanHash = build.PlanHash,
            ContractVersion = plan.PlanContractVersion,
            AnswerSetFingerprint = "manual",
            ApplyGate = new VisionAgentApplyGate
            {
                CanvasApplyReady = true,
                RuntimeDraftReady = true,
                DeploymentReady = false,
                Status = "ready",
                MetadataOnly = true
            },
            WorkflowDiff = new VisionAgentWorkflowDiff
            {
                AddedNodes = ["op_camera"],
                MetadataOnly = true
            },
            ToolEvidenceTimeline =
            [
                new VisionAgentToolEvidence
                {
                    Stage = "fake_build",
                    ToolName = "fake_build",
                    Source = "workflow_compiler",
                    Status = "completed"
                }
            ],
            MetadataOnly = true
        };

        return new AiFlowGenerationResult
        {
            Success = true,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
            Flow = null,
            AiExplanation = "fake build completed",
            BuildResult = buildResult,
            BuildReadiness = new VisionAgentBuildReadinessSnapshot
            {
                CanBuild = true,
                ContractVersion = plan.PlanContractVersion,
                ResolvedFields = plan.ResolvedPlanFields
            },
            InteractionState = AiInteractionStates.Completed,
            TurnIntent = AiTurnIntents.NewFlow,
            RouterConfidence = AiRouterConfidence.High
        };
    }

    private sealed record EntryResult(
        string RunId,
        AgentRunEvent? Terminal,
        AgentRunReplayResult Replay);

    private sealed record BuildHarness(
        string Directory,
        AgentRunEventRedactor Redactor,
        AgentRunEventStore Store,
        AgentRunEventStreamService Stream,
        VisionAgentBuildProjectionJournal Journal,
        ConversationalFlowService Conversation,
        VisionAgentBuildTerminalProjector Projector,
        VisionAgentBuildRunService RunService) : IDisposable
    {
        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }

    private sealed class ThrowOnceConversationService : IConversationalFlowService
    {
        private readonly IConversationalFlowService _inner;
        private bool _throw = true;

        public ThrowOnceConversationService(IConversationalFlowService inner)
        {
            _inner = inner;
        }

        public ConversationSession GetOrCreateSession(string? sessionId) => _inner.GetOrCreateSession(sessionId);

        public ConversationSession GetOrCreateSession(string ownerHash, string? sessionId) =>
            _inner.GetOrCreateSession(ownerHash, sessionId);

        public ConversationIntent DetectIntent(string userDescription, bool hasExistingFlow) =>
            _inner.DetectIntent(userDescription, hasExistingFlow);

        public ConversationContext PrepareContext(AiFlowGenerationRequest request) =>
            _inner.PrepareContext(request);

        public ConversationContext PrepareContext(string ownerHash, AiFlowGenerationRequest request) =>
            _inner.PrepareContext(ownerHash, request);

        public void RecordAssistantResponse(
            string sessionId,
            string assistantMessage,
            string? latestFlowJson,
            string? latestCanvasFlowJson = null,
            ConversationTurnPayload? payload = null)
        {
            if (_throw)
            {
                _throw = false;
                throw new InvalidOperationException("public failure");
            }

            _inner.RecordAssistantResponse(sessionId, assistantMessage, latestFlowJson, latestCanvasFlowJson, payload);
        }

        public ConversationSessionWriteResult RecordAssistantResponseWithPersistence(
            string ownerHash,
            string sessionId,
            string assistantMessage,
            string? latestFlowJson,
            string? latestCanvasFlowJson = null,
            ConversationTurnPayload? payload = null)
        {
            if (_throw)
            {
                _throw = false;
                throw new InvalidOperationException("public failure");
            }

            return _inner.RecordAssistantResponseWithPersistence(
                ownerHash,
                sessionId,
                assistantMessage,
                latestFlowJson,
                latestCanvasFlowJson,
                payload);
        }

        public ConversationSessionWriteResult RecordAssistantResponseWithPersistence(
            string sessionId,
            string assistantMessage,
            string? latestFlowJson,
            string? latestCanvasFlowJson = null,
            ConversationTurnPayload? payload = null)
        {
            if (_throw)
            {
                _throw = false;
                throw new InvalidOperationException("public failure");
            }

            return _inner.RecordAssistantResponseWithPersistence(
                sessionId,
                assistantMessage,
                latestFlowJson,
                latestCanvasFlowJson,
                payload);
        }

        public IReadOnlyList<ConversationSessionSummary> ListSessions() => _inner.ListSessions();

        public IReadOnlyList<ConversationSessionSummary> ListSessions(string ownerHash) =>
            _inner.ListSessions(ownerHash);

        public IReadOnlyList<ConversationSessionSummary> ListSessionsForRecovery() =>
            _inner.ListSessionsForRecovery();

        public ConversationSession? GetSession(string sessionId) => _inner.GetSession(sessionId);

        public ConversationSession? GetSession(string ownerHash, string sessionId) =>
            _inner.GetSession(ownerHash, sessionId);

        public ConversationSession? GetSessionForRecovery(string sessionId) =>
            _inner.GetSessionForRecovery(sessionId);

        public bool TryBackfillCanvasFlowJson(string sessionId, string canvasFlowJson) =>
            _inner.TryBackfillCanvasFlowJson(sessionId, canvasFlowJson);

        public bool TryBackfillCanvasFlowJson(string ownerHash, string sessionId, string canvasFlowJson) =>
            _inner.TryBackfillCanvasFlowJson(ownerHash, sessionId, canvasFlowJson);

        public ConversationBackfillResult TryBackfillCanvasFlowJsonWithResult(string sessionId, string canvasFlowJson) =>
            _inner.TryBackfillCanvasFlowJsonWithResult(sessionId, canvasFlowJson);

        public ConversationBackfillResult TryBackfillCanvasFlowJsonWithResult(
            string ownerHash,
            string sessionId,
            string canvasFlowJson) =>
            _inner.TryBackfillCanvasFlowJsonWithResult(ownerHash, sessionId, canvasFlowJson);

        public ConversationSession UpdateWorkspaceSnapshot(string sessionId, VisionAgentWorkspaceSnapshotUpdate update) =>
            _inner.UpdateWorkspaceSnapshot(sessionId, update);

        public VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshot(
            string sessionId,
            VisionAgentWorkspaceSnapshotUpdate update) =>
            _inner.TryUpdateWorkspaceSnapshot(sessionId, update);

        public VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshot(
            string ownerHash,
            string sessionId,
            VisionAgentWorkspaceSnapshotUpdate update) =>
            _inner.TryUpdateWorkspaceSnapshot(ownerHash, sessionId, update);

        public VisionAgentWorkspaceSnapshotMutationResult TryInitializeWorkspaceSnapshot(
            string ownerHash,
            string sessionId,
            VisionAgentWorkspaceSnapshotUpdate update) =>
            _inner.TryInitializeWorkspaceSnapshot(ownerHash, sessionId, update);

        public VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshotForRecovery(
            string ownerHash,
            string sessionId,
            VisionAgentWorkspaceSnapshotUpdate update) =>
            _inner.TryUpdateWorkspaceSnapshotForRecovery(ownerHash, sessionId, update);

        public VisionAgentWorkspaceSnapshotMutationResult TryBeginAgentRun(
            string sessionId,
            string runId,
            string kind,
            string? clientMutationId = null) =>
            _inner.TryBeginAgentRun(sessionId, runId, kind, clientMutationId);

        public VisionAgentWorkspaceSnapshotMutationResult TryBeginAgentRun(
            string ownerHash,
            string sessionId,
            string runId,
            string kind,
            string? clientMutationId = null) =>
            _inner.TryBeginAgentRun(ownerHash, sessionId, runId, kind, clientMutationId);

        public VisionAgentWorkspaceSnapshotMutationResult ProjectBuildTerminal(VisionAgentTerminalProjectionRequest request)
        {
            if (_throw)
            {
                _throw = false;
                throw new InvalidOperationException("public failure");
            }

            return _inner.ProjectBuildTerminal(request);
        }

        public VisionAgentWorkspaceSnapshotMutationResult ProjectBuildTerminal(
            string ownerHash,
            VisionAgentTerminalProjectionRequest request)
        {
            if (_throw)
            {
                _throw = false;
                throw new InvalidOperationException("public failure");
            }

            return _inner.ProjectBuildTerminal(ownerHash, request);
        }

        public ConversationPersistenceStatus GetLastPersistenceStatus() => _inner.GetLastPersistenceStatus();

        public bool DeleteSession(string sessionId) => _inner.DeleteSession(sessionId);

        public ConversationSessionDeleteResult DeleteSessionWithResult(string sessionId) =>
            _inner.DeleteSessionWithResult(sessionId);

        public ConversationSessionDeleteResult DeleteSessionWithResult(string ownerHash, string sessionId) =>
            _inner.DeleteSessionWithResult(ownerHash, sessionId);
    }

    private sealed class PartialProjectionConversationService : IConversationalFlowService
    {
        private readonly IConversationalFlowService _inner;
        private bool _failOnce = true;

        public PartialProjectionConversationService(IConversationalFlowService inner)
        {
            _inner = inner;
        }

        public ConversationSession GetOrCreateSession(string? sessionId) => _inner.GetOrCreateSession(sessionId);

        public ConversationSession GetOrCreateSession(string ownerHash, string? sessionId) =>
            _inner.GetOrCreateSession(ownerHash, sessionId);

        public ConversationIntent DetectIntent(string userDescription, bool hasExistingFlow) =>
            _inner.DetectIntent(userDescription, hasExistingFlow);

        public ConversationContext PrepareContext(AiFlowGenerationRequest request) =>
            _inner.PrepareContext(request);

        public ConversationContext PrepareContext(string ownerHash, AiFlowGenerationRequest request) =>
            _inner.PrepareContext(ownerHash, request);

        public void RecordAssistantResponse(
            string sessionId,
            string assistantMessage,
            string? latestFlowJson,
            string? latestCanvasFlowJson = null,
            ConversationTurnPayload? payload = null) =>
            _inner.RecordAssistantResponse(sessionId, assistantMessage, latestFlowJson, latestCanvasFlowJson, payload);

        public ConversationSessionWriteResult RecordAssistantResponseWithPersistence(
            string ownerHash,
            string sessionId,
            string assistantMessage,
            string? latestFlowJson,
            string? latestCanvasFlowJson = null,
            ConversationTurnPayload? payload = null) =>
            _inner.RecordAssistantResponseWithPersistence(
                ownerHash,
                sessionId,
                assistantMessage,
                latestFlowJson,
                latestCanvasFlowJson,
                payload);

        public ConversationSessionWriteResult RecordAssistantResponseWithPersistence(
            string sessionId,
            string assistantMessage,
            string? latestFlowJson,
            string? latestCanvasFlowJson = null,
            ConversationTurnPayload? payload = null) =>
            _inner.RecordAssistantResponseWithPersistence(
                sessionId,
                assistantMessage,
                latestFlowJson,
                latestCanvasFlowJson,
                payload);

        public IReadOnlyList<ConversationSessionSummary> ListSessions() => _inner.ListSessions();

        public IReadOnlyList<ConversationSessionSummary> ListSessions(string ownerHash) =>
            _inner.ListSessions(ownerHash);

        public IReadOnlyList<ConversationSessionSummary> ListSessionsForRecovery() =>
            _inner.ListSessionsForRecovery();

        public ConversationSession? GetSession(string sessionId) => _inner.GetSession(sessionId);

        public ConversationSession? GetSession(string ownerHash, string sessionId) =>
            _inner.GetSession(ownerHash, sessionId);

        public ConversationSession? GetSessionForRecovery(string sessionId) =>
            _inner.GetSessionForRecovery(sessionId);

        public bool TryBackfillCanvasFlowJson(string sessionId, string canvasFlowJson) =>
            _inner.TryBackfillCanvasFlowJson(sessionId, canvasFlowJson);

        public bool TryBackfillCanvasFlowJson(string ownerHash, string sessionId, string canvasFlowJson) =>
            _inner.TryBackfillCanvasFlowJson(ownerHash, sessionId, canvasFlowJson);

        public ConversationBackfillResult TryBackfillCanvasFlowJsonWithResult(string sessionId, string canvasFlowJson) =>
            _inner.TryBackfillCanvasFlowJsonWithResult(sessionId, canvasFlowJson);

        public ConversationBackfillResult TryBackfillCanvasFlowJsonWithResult(
            string ownerHash,
            string sessionId,
            string canvasFlowJson) =>
            _inner.TryBackfillCanvasFlowJsonWithResult(ownerHash, sessionId, canvasFlowJson);

        public ConversationSession UpdateWorkspaceSnapshot(string sessionId, VisionAgentWorkspaceSnapshotUpdate update) =>
            _inner.UpdateWorkspaceSnapshot(sessionId, update);

        public VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshot(
            string sessionId,
            VisionAgentWorkspaceSnapshotUpdate update) =>
            _inner.TryUpdateWorkspaceSnapshot(sessionId, update);

        public VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshot(
            string ownerHash,
            string sessionId,
            VisionAgentWorkspaceSnapshotUpdate update) =>
            _inner.TryUpdateWorkspaceSnapshot(ownerHash, sessionId, update);

        public VisionAgentWorkspaceSnapshotMutationResult TryInitializeWorkspaceSnapshot(
            string ownerHash,
            string sessionId,
            VisionAgentWorkspaceSnapshotUpdate update) =>
            _inner.TryInitializeWorkspaceSnapshot(ownerHash, sessionId, update);

        public VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshotForRecovery(
            string ownerHash,
            string sessionId,
            VisionAgentWorkspaceSnapshotUpdate update) =>
            _inner.TryUpdateWorkspaceSnapshotForRecovery(ownerHash, sessionId, update);

        public VisionAgentWorkspaceSnapshotMutationResult TryBeginAgentRun(
            string sessionId,
            string runId,
            string kind,
            string? clientMutationId = null) =>
            _inner.TryBeginAgentRun(sessionId, runId, kind, clientMutationId);

        public VisionAgentWorkspaceSnapshotMutationResult TryBeginAgentRun(
            string ownerHash,
            string sessionId,
            string runId,
            string kind,
            string? clientMutationId = null) =>
            _inner.TryBeginAgentRun(ownerHash, sessionId, runId, kind, clientMutationId);

        public VisionAgentWorkspaceSnapshotMutationResult ProjectBuildTerminal(VisionAgentTerminalProjectionRequest request)
        {
            if (!_failOnce)
            {
                return _inner.ProjectBuildTerminal(request);
            }

            _failOnce = false;
            var partial = _inner.ProjectBuildTerminal(request);

            return new VisionAgentWorkspaceSnapshotMutationResult
            {
                Success = false,
                ErrorCode = "primary_store_save_failed",
                PublicMessage = "simulated snapshot failure",
                Snapshot = partial.Snapshot,
                PersistenceStatus = new ConversationPersistenceStatus
                {
                    PrimaryStoreSaved = false,
                    RecoveryBackupSaved = true,
                    ErrorCode = "primary_store_save_failed",
                    PublicMessage = "simulated snapshot failure"
                }
            };
        }

        public VisionAgentWorkspaceSnapshotMutationResult ProjectBuildTerminal(
            string ownerHash,
            VisionAgentTerminalProjectionRequest request)
        {
            if (!_failOnce)
            {
                return _inner.ProjectBuildTerminal(ownerHash, request);
            }

            _failOnce = false;
            var partial = _inner.ProjectBuildTerminal(ownerHash, request);

            return new VisionAgentWorkspaceSnapshotMutationResult
            {
                Success = false,
                ErrorCode = "primary_store_save_failed",
                PublicMessage = "simulated snapshot failure",
                Snapshot = partial.Snapshot,
                PersistenceStatus = new ConversationPersistenceStatus
                {
                    PrimaryStoreSaved = false,
                    RecoveryBackupSaved = true,
                    ErrorCode = "primary_store_save_failed",
                    PublicMessage = "simulated snapshot failure"
                }
            };
        }

        public ConversationPersistenceStatus GetLastPersistenceStatus() => _inner.GetLastPersistenceStatus();

        public bool DeleteSession(string sessionId) => _inner.DeleteSession(sessionId);

        public ConversationSessionDeleteResult DeleteSessionWithResult(string sessionId) =>
            _inner.DeleteSessionWithResult(sessionId);

        public ConversationSessionDeleteResult DeleteSessionWithResult(string ownerHash, string sessionId) =>
            _inner.DeleteSessionWithResult(ownerHash, sessionId);
    }

    private sealed class FakeBuildExecution : IVisionAgentOrchestrator
    {
        public Task<VisionAgentPlanModeResult> CreatePlanAsync(
            VisionAgentPlanModeRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<AiFlowGenerationResult> BuildFromPlanAsync(
            AiFlowGenerationRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(SuccessResult(request));
        }
    }
}
