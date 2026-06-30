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

public sealed class BuildFromPlanEntryParityTests : IDisposable
{
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
    public async Task ToolLoopMode_ShouldMatchAcrossAgentRunWebMessageAndInternalEntries()
    {
        using var harness = CreateHarness();
        var plan = BuildPlan();

        var agentRun = await RunAgentRunEntryAsync(harness, BuildRequest(plan) with
        {
            SessionId = "session-tool-agent",
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.ToolLoop
        });
        var webMessage = await RunWebMessageEntryAsync(harness, BuildRequest(plan) with
        {
            SessionId = "session-tool-web",
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.ToolLoop
        });
        var internalEntry = await RunInternalEntryAsync(harness, BuildRequest(plan) with
        {
            SessionId = "session-tool-internal",
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.ToolLoop
        });

        var expected = TerminalBusinessProjection(agentRun.Replay);
        expected.RequestedMode.Should().Be(AiAgentGenerateFlowModes.ToolLoop);
        expected.EffectiveMode.Should().Be(AiAgentGenerateFlowModes.ToolLoop);
        expected.ToolLoopEntered.Should().BeTrue();
        TerminalBusinessProjection(webMessage.Replay).Should().BeEquivalentTo(expected);
        TerminalBusinessProjection(internalEntry.Replay).Should().BeEquivalentTo(expected);
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
        var run = harness.Stream.CreateRun("startup recovery", new { sessionId = "session-startup", metadataOnly = true });
        var request = BuildRequest(BuildPlan()) with
        {
            AgentRunId = run.RunId,
            SessionId = "session-startup"
        };
        var result = SuccessResult(request);
        harness.Stream.Complete(run.RunId, "done", new
        {
            status = result.CompletionStatus,
            sessionId = request.SessionId,
            flow = result.Flow,
            buildResult = result.BuildResult,
            buildReadiness = result.BuildReadiness,
            metadataOnly = true
        }).Should().NotBeNull();
        harness.Conversation.GetSession("session-startup").Should().BeNull();

        var services = new ServiceCollection();
        services.AddSingleton<IVisionAgentBuildTerminalProjector>(
            new VisionAgentBuildTerminalProjector(
                harness.Conversation,
                harness.Journal,
                Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildTerminalProjector>>()));
        await using var provider = services.BuildServiceProvider();
        var reconciler = new VisionAgentBuildProjectionReconciliationService(
            harness.Store,
            new AgentRunEventStreamService(harness.Store, harness.Redactor),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentBuildProjectionReconciliationService>>());

        await reconciler.ReconcileAsync(CancellationToken.None);
        harness.Conversation.GetSession("session-startup")!.History.Should().HaveCount(1);
        var checkpoint = harness.Journal.LoadCheckpoints().Single(item => item.RunId == run.RunId);
        checkpoint.Status.Should().Be(VisionAgentBuildProjectionStatuses.Projected);
        checkpoint.TerminalMutationId.Should().StartWith("build-terminal:build:");
        checkpoint.PayloadFingerprint.Should().StartWith("sha256:");
        checkpoint.Identity.Should().NotBeNullOrWhiteSpace();
        await reconciler.ReconcileAsync(CancellationToken.None);

        harness.Conversation.GetSession("session-startup")!.History.Should().HaveCount(1);
        harness.Journal.LoadCheckpoints()
            .Single(item => item.RunId == run.RunId)
            .Status.Should()
            .Be(VisionAgentBuildProjectionStatuses.Projected);
    }

    [Fact]
    public async Task StartupRecovery_ShouldCompletePreparedPlanTerminalIntentOnce()
    {
        using var harness = CreateHarness();
        var sessionId = "session-plan-recovery";
        var plan = BuildPlan();
        var run = harness.Stream.CreateRun("plan startup recovery", new
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
    public void ProjectorRetry_ShouldRepairPartialProjectionWithoutDuplicatingStableAssistantTurn()
    {
        using var harness = CreateHarness();
        var run = harness.Stream.CreateRun("partial projection", new { sessionId = "session-partial", metadataOnly = true });
        var request = BuildRequest(BuildPlan()) with
        {
            AgentRunId = run.RunId,
            SessionId = "session-partial"
        };
        var result = SuccessResult(request);
        var terminal = harness.Stream.Complete(run.RunId, "done", new
        {
            status = result.CompletionStatus,
            sessionId = request.SessionId,
            flow = result.Flow,
            buildResult = result.BuildResult,
            buildReadiness = result.BuildReadiness,
            metadataOnly = true
        })!;
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
        var run = harness.Stream.CreateRun("projection retry", new { sessionId = "session-retry", metadataOnly = true });
        var request = BuildRequest(BuildPlan()) with
        {
            SessionId = "session-retry",
            AgentRunId = run.RunId
        };
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
        var run = harness.Stream.CreateRun("session policy", new { metadataOnly = true });
        var terminal = harness.Stream.Cancel(run.RunId)!;
        var request = BuildRequest(BuildPlan()) with
        {
            SessionId = "bad/path",
            AgentRunId = run.RunId
        };
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
                Enabled = enabled,
                Mode = AiAgentGenerateFlowModes.Scripted
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

    private static async Task<EntryResult> RunAgentRunEntryAsync(
        BuildHarness harness,
        AiFlowGenerationRequest request)
    {
        var create = harness.Stream.CreateRun(request.Description, new
        {
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
        var service = CreateInternalGenerationService(harness);
        var result = await service.GenerateFlowAsync(request, cancellationToken: CancellationToken.None);
        result.SessionId.Should().Be(request.SessionId);
        result.CompletionStatus.Should().NotBeNullOrWhiteSpace();
        result.PlanId.Should().NotBeNullOrWhiteSpace();

        var runId = harness.Stream.ReplayLatest()?.Summary.RunId;
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
                Enabled = true,
                Mode = AiAgentGenerateFlowModes.Scripted,
                FallbackToLegacyOnFailure = false
            }),
            Substitute.For<IVisionAgentGenerateFlowService>(),
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
            OperatorCatalogVersion = plan.OperatorCatalogVersion,
            StationBoundarySummary = plan.StationBoundarySummary,
            PlcOutputPolicy = plan.PlcOutputPolicy,
            BuildIntent = "new",
            OriginalUserPrompt = plan.OriginalUserPrompt
        };
        build = mutate?.Invoke(build) ?? build;
        return new AiFlowGenerationRequest("detect scratches", Mode: GenerateFlowMode.New)
        {
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
                    Source = string.Equals(
                        request.AgentGenerateFlowMode,
                        AiAgentGenerateFlowModes.ToolLoop,
                        StringComparison.OrdinalIgnoreCase)
                        ? "tool_loop"
                        : "fixed_build_orchestrator",
                    Status = "completed"
                }
            ],
            MetadataOnly = true
        };

        return new AiFlowGenerationResult
        {
            Success = true,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
            Flow = new OperatorFlowDto(),
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

        public ConversationIntent DetectIntent(string userDescription, bool hasExistingFlow) =>
            _inner.DetectIntent(userDescription, hasExistingFlow);

        public ConversationContext PrepareContext(AiFlowGenerationRequest request) =>
            _inner.PrepareContext(request);

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

        public ConversationSession? GetSession(string sessionId) => _inner.GetSession(sessionId);

        public bool TryBackfillCanvasFlowJson(string sessionId, string canvasFlowJson) =>
            _inner.TryBackfillCanvasFlowJson(sessionId, canvasFlowJson);

        public ConversationBackfillResult TryBackfillCanvasFlowJsonWithResult(string sessionId, string canvasFlowJson) =>
            _inner.TryBackfillCanvasFlowJsonWithResult(sessionId, canvasFlowJson);

        public ConversationSession UpdateWorkspaceSnapshot(string sessionId, VisionAgentWorkspaceSnapshotUpdate update) =>
            _inner.UpdateWorkspaceSnapshot(sessionId, update);

        public VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshot(
            string sessionId,
            VisionAgentWorkspaceSnapshotUpdate update) =>
            _inner.TryUpdateWorkspaceSnapshot(sessionId, update);

        public VisionAgentWorkspaceSnapshotMutationResult ProjectBuildTerminal(VisionAgentTerminalProjectionRequest request)
        {
            if (_throw)
            {
                _throw = false;
                throw new InvalidOperationException("public failure");
            }

            return _inner.ProjectBuildTerminal(request);
        }

        public ConversationPersistenceStatus GetLastPersistenceStatus() => _inner.GetLastPersistenceStatus();

        public bool DeleteSession(string sessionId) => _inner.DeleteSession(sessionId);

        public ConversationSessionDeleteResult DeleteSessionWithResult(string sessionId) =>
            _inner.DeleteSessionWithResult(sessionId);
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

        public ConversationIntent DetectIntent(string userDescription, bool hasExistingFlow) =>
            _inner.DetectIntent(userDescription, hasExistingFlow);

        public ConversationContext PrepareContext(AiFlowGenerationRequest request) =>
            _inner.PrepareContext(request);

        public void RecordAssistantResponse(
            string sessionId,
            string assistantMessage,
            string? latestFlowJson,
            string? latestCanvasFlowJson = null,
            ConversationTurnPayload? payload = null) =>
            _inner.RecordAssistantResponse(sessionId, assistantMessage, latestFlowJson, latestCanvasFlowJson, payload);

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

        public ConversationSession? GetSession(string sessionId) => _inner.GetSession(sessionId);

        public bool TryBackfillCanvasFlowJson(string sessionId, string canvasFlowJson) =>
            _inner.TryBackfillCanvasFlowJson(sessionId, canvasFlowJson);

        public ConversationBackfillResult TryBackfillCanvasFlowJsonWithResult(string sessionId, string canvasFlowJson) =>
            _inner.TryBackfillCanvasFlowJsonWithResult(sessionId, canvasFlowJson);

        public ConversationSession UpdateWorkspaceSnapshot(string sessionId, VisionAgentWorkspaceSnapshotUpdate update) =>
            _inner.UpdateWorkspaceSnapshot(sessionId, update);

        public VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshot(
            string sessionId,
            VisionAgentWorkspaceSnapshotUpdate update) =>
            _inner.TryUpdateWorkspaceSnapshot(sessionId, update);

        public VisionAgentWorkspaceSnapshotMutationResult ProjectBuildTerminal(VisionAgentTerminalProjectionRequest request)
        {
            if (!_failOnce)
            {
                return _inner.ProjectBuildTerminal(request);
            }

            _failOnce = false;
            var partial = _inner.ProjectBuildTerminal(new VisionAgentTerminalProjectionRequest
            {
                SessionId = request.SessionId,
                AssistantTurnId = request.AssistantTurnId,
                AssistantMessage = request.AssistantMessage,
                LatestFlowJson = request.LatestFlowJson,
                LatestCanvasFlowJson = request.LatestCanvasFlowJson,
                Payload = request.Payload,
                WorkspaceUpdate = new VisionAgentWorkspaceSnapshotUpdate
                {
                    LifecycleState = "build_projection_partial"
                }
            });

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
