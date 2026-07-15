using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI;
using FluentAssertions;
using System.Text.Json;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public class ConversationalFlowServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public ConversationalFlowServiceTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "clearvision-conversation-history-test-" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void ListSessions_ReturnsOrderedSummaries()
    {
        var service = new ConversationalFlowService(_tempRoot);

        var firstContext = service.PrepareContext(new AiFlowGenerationRequest(
            "创建第一条流程",
            SessionId: "session-a"));
        service.RecordAssistantResponse(firstContext.SessionId, "assistant first", "{\"explanation\":\"first\"}");

        Thread.Sleep(25);

        var secondContext = service.PrepareContext(new AiFlowGenerationRequest(
            "创建第二条流程",
            SessionId: "session-b"));
        service.RecordAssistantResponse(secondContext.SessionId, "assistant second", "{\"explanation\":\"second\"}");

        var sessions = service.ListSessions();

        sessions.Should().HaveCount(2);
        sessions[0].SessionId.Should().Be("session-b");
        sessions[1].SessionId.Should().Be("session-a");
        sessions[0].LastMessage.Should().Contain("assistant second");
        sessions[0].TurnCount.Should().Be(2);
        sessions[1].TurnCount.Should().Be(2);
    }

    [Fact]
    public void GetSession_ValidId_ReturnsFullData()
    {
        var service = new ConversationalFlowService(_tempRoot);

        var context = service.PrepareContext(new AiFlowGenerationRequest(
            "生成流程",
            SessionId: "session-detail"));

        const string latestFlowJson = "{\"explanation\":\"restored explanation\",\"operators\":[{\"displayName\":\"Threshold\"}] }";
        const string latestCanvasFlowJson = "{\"operators\":[{\"id\":\"op-1\",\"type\":\"Thresholding\",\"name\":\"Threshold\",\"inputPorts\":[],\"outputPorts\":[]}],\"connections\":[]}";
        service.RecordAssistantResponse(context.SessionId, "assistant detail", latestFlowJson, latestCanvasFlowJson);

        var session = service.GetSession("session-detail");

        session.Should().NotBeNull();
        session!.SessionId.Should().Be("session-detail");
        session.History.Should().HaveCount(2);
        session.CurrentFlowJson.Should().Be(latestFlowJson);
        session.CurrentCanvasFlowJson.Should().Be(latestCanvasFlowJson);
    }

    [Fact]
    public void RecordAssistantResponse_WhenLatestFlowIsCanvasJson_ShouldPopulateCanvasSnapshot()
    {
        var service = new ConversationalFlowService(_tempRoot);

        var context = service.PrepareContext(new AiFlowGenerationRequest(
            "创建流程",
            SessionId: "session-canvas-only"));

        const string canvasJson = "{\"operators\":[{\"id\":\"op-1\",\"type\":\"ResultOutput\"}],\"connections\":[]}";
        service.RecordAssistantResponse(context.SessionId, "assistant canvas", canvasJson);

        var session = service.GetSession(context.SessionId);
        session.Should().NotBeNull();
        session!.CurrentFlowJson.Should().Be(canvasJson);
        session.CurrentCanvasFlowJson.Should().Be(canvasJson);
    }

    [Fact]
    public void GetSession_InvalidId_ReturnsNull()
    {
        var service = new ConversationalFlowService(_tempRoot);

        var session = service.GetSession("not-exists");

        session.Should().BeNull();
    }

    [Fact]
    public void DeleteSession_RemovesAndPersists()
    {
        var service = new ConversationalFlowService(_tempRoot);

        var context = service.PrepareContext(new AiFlowGenerationRequest(
            "创建待删除流程",
            SessionId: "session-delete"));
        service.RecordAssistantResponse(context.SessionId, "assistant delete", "{\"explanation\":\"delete\"}");

        service.DeleteSession("session-delete").Should().BeTrue();
        service.ListSessions().Should().NotContain(summary => summary.SessionId == "session-delete");

        var reloadedService = new ConversationalFlowService(_tempRoot);
        reloadedService.ListSessions().Should().NotContain(summary => summary.SessionId == "session-delete");
    }

    [Fact]
    public void DeleteSession_WhenPrimaryStoreFails_ShouldKeepMemoryAndDiskSession()
    {
        var service = new ConversationalFlowService(_tempRoot);

        var context = service.PrepareContext(new AiFlowGenerationRequest(
            "create flow for delete",
            SessionId: "session-delete-primary-fail"));
        service.RecordAssistantResponse(context.SessionId, "assistant delete", "{\"explanation\":\"delete\"}");

        service.PrimaryStoreWriteFaultInjector = () => throw new IOException("primary failed");
        var result = service.DeleteSessionWithResult("session-delete-primary-fail");

        result.Status.Should().Be(ConversationSessionDeleteStatus.PersistenceFailed);
        result.PersistenceStatus.PrimaryStoreSaved.Should().BeFalse();
        service.GetSession("session-delete-primary-fail").Should().NotBeNull();
        new ConversationalFlowService(_tempRoot).GetSession("session-delete-primary-fail").Should().NotBeNull();
    }

    [Fact]
    public void TryBackfillCanvasFlowJson_ShouldPersistCanvasSnapshotForLegacySession()
    {
        var service = new ConversationalFlowService(_tempRoot);

        var context = service.PrepareContext(new AiFlowGenerationRequest(
            "恢复历史会话",
            SessionId: "session-legacy"));

        const string legacyAiRawJson = "{\"Explanation\":\"legacy\",\"Operators\":[{\"TempId\":\"op_1\",\"OperatorType\":\"ImageAcquisition\",\"DisplayName\":\"采集\",\"Parameters\":{}}],\"Connections\":[]}";
        service.RecordAssistantResponse(context.SessionId, "assistant legacy", legacyAiRawJson);

        const string canvasJson = "{\"operators\":[{\"id\":\"op-1\",\"type\":\"ImageAcquisition\",\"name\":\"采集\",\"inputPorts\":[],\"outputPorts\":[]}],\"connections\":[]}";
        service.TryBackfillCanvasFlowJson(context.SessionId, canvasJson).Should().BeTrue();

        var session = service.GetSession(context.SessionId);
        session.Should().NotBeNull();
        session!.CurrentCanvasFlowJson.Should().Be(canvasJson);

        var reloadedService = new ConversationalFlowService(_tempRoot);
        var reloaded = reloadedService.GetSession(context.SessionId);
        reloaded.Should().NotBeNull();
        reloaded!.CurrentCanvasFlowJson.Should().Be(canvasJson);
    }

    [Fact]
    public void TryBackfillCanvasFlowJson_WhenPrimaryStoreFails_ShouldKeepMemoryAndDiskUnchanged()
    {
        var service = new ConversationalFlowService(_tempRoot);

        var context = service.PrepareContext(new AiFlowGenerationRequest(
            "restore legacy session",
            SessionId: "session-backfill-primary-fail"));
        service.RecordAssistantResponse(
            context.SessionId,
            "assistant legacy",
            "{\"Explanation\":\"legacy\",\"Operators\":[{\"TempId\":\"op_1\",\"OperatorType\":\"ImageAcquisition\"}],\"Connections\":[]}");

        service.PrimaryStoreWriteFaultInjector = () => throw new IOException("primary failed");
        var result = service.TryBackfillCanvasFlowJsonWithResult(
            context.SessionId,
            "{\"operators\":[{\"id\":\"op-1\",\"type\":\"ImageAcquisition\"}],\"connections\":[]}");

        result.Status.Should().Be(ConversationBackfillStatus.PersistenceFailed);
        result.PersistenceStatus.PrimaryStoreSaved.Should().BeFalse();
        service.GetSession(context.SessionId)!.CurrentCanvasFlowJson.Should().BeNullOrWhiteSpace();
        new ConversationalFlowService(_tempRoot)
            .GetSession(context.SessionId)!
            .CurrentCanvasFlowJson.Should().BeNullOrWhiteSpace();
    }

    [Fact]
    public void PrepareContext_WhenPrimaryStoreFails_ShouldNotPublishEmptySession()
    {
        var service = new ConversationalFlowService(_tempRoot);
        service.PrimaryStoreWriteFaultInjector = () => throw new IOException("primary failed");

        var act = () => service.PrepareContext(new AiFlowGenerationRequest(
            "create a flow",
            SessionId: "session-prepare-primary-fail"));

        act.Should().Throw<IOException>();
        service.GetSession("session-prepare-primary-fail").Should().BeNull();
        new ConversationalFlowService(_tempRoot).GetSession("session-prepare-primary-fail").Should().BeNull();
    }

    [Fact]
    public void RecordAssistantResponse_WhenPrimaryStoreFails_ShouldKeepMemoryAndDiskHistoryUnchanged()
    {
        var service = new ConversationalFlowService(_tempRoot);

        var context = service.PrepareContext(new AiFlowGenerationRequest(
            "create a flow",
            SessionId: "session-assistant-primary-fail"));

        service.PrimaryStoreWriteFaultInjector = () => throw new IOException("primary failed");
        var result = service.RecordAssistantResponseWithPersistence(
            context.SessionId,
            "assistant failed",
            "{\"explanation\":\"failed\"}");

        result.Success.Should().BeFalse();
        result.PersistenceStatus.PrimaryStoreSaved.Should().BeFalse();
        service.GetSession(context.SessionId)!.History.Should().ContainSingle(turn => turn.Role == "user");
        new ConversationalFlowService(_tempRoot)
            .GetSession(context.SessionId)!
            .History.Should().ContainSingle(turn => turn.Role == "user");
    }

    [Fact]
    public void PrepareContext_WithExplicitMode_ShouldPreferRequestedMode()
    {
        var service = new ConversationalFlowService(_tempRoot);

        var context = service.PrepareContext(new AiFlowGenerationRequest(
            "解释一下当前流程",
            SessionId: "session-explicit",
            Mode: GenerateFlowMode.Explain));

        context.Mode.Should().Be(GenerateFlowMode.Explain);
        context.Intent.Should().Be(ConversationIntent.Explain);
    }

    [Fact]
    public void PrepareContext_WithEmptyFlowPayload_ShouldTreatAsNewAndClearExistingFlow()
    {
        var service = new ConversationalFlowService(_tempRoot);

        service.PrepareContext(new AiFlowGenerationRequest(
            "先生成一个流程",
            SessionId: "session-empty-flow",
            ExistingFlowJson: """{"operators":[{"id":"op-1","type":"Thresholding"}],"connections":[]}"""));

        var context = service.PrepareContext(new AiFlowGenerationRequest(
            "修改一下",
            SessionId: "session-empty-flow",
            ExistingFlowJson: """{"operators":[],"connections":[]}""",
            Mode: GenerateFlowMode.Auto));

        context.Mode.Should().Be(GenerateFlowMode.New);
        context.Intent.Should().Be(ConversationIntent.New);
        context.ExistingFlowJson.Should().BeNull();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"null\"")]
    public void PrepareContext_WithNonObjectFlowPayload_ShouldTreatAsNewAndClearExistingFlow(string existingFlowJson)
    {
        var service = new ConversationalFlowService(_tempRoot);

        service.PrepareContext(new AiFlowGenerationRequest(
            "先生成一个流程",
            SessionId: "session-non-object-flow",
            ExistingFlowJson: """{"operators":[{"id":"op-1","type":"Thresholding"}],"connections":[]}"""));

        var context = service.PrepareContext(new AiFlowGenerationRequest(
            "修改一下",
            SessionId: "session-non-object-flow",
            ExistingFlowJson: existingFlowJson,
            Mode: GenerateFlowMode.Auto));

        context.Mode.Should().Be(GenerateFlowMode.New);
        context.Intent.Should().Be(ConversationIntent.New);
        context.ExistingFlowJson.Should().BeNull();
    }

    [Fact]
    public void PrepareContext_ShouldBuildSessionSummaryWithoutWorkflowJson()
    {
        var service = new ConversationalFlowService(_tempRoot);

        var first = service.PrepareContext(new AiFlowGenerationRequest(
            "创建流程",
            SessionId: "session-summary"));
        service.RecordAssistantResponse(
            first.SessionId,
            "已生成草案。\n```json\n{\"operators\":[1]}\n```",
            "{\"operators\":[{\"tempId\":\"op_1\"}],\"connections\":[]}");

        var second = service.PrepareContext(new AiFlowGenerationRequest(
            "继续优化参数",
            SessionId: "session-summary"));

        second.SessionSummary.Should().Contain("- user: 创建流程");
        second.SessionSummary.Should().Contain("- assistant: 已生成草案。");
        second.SessionSummary.Should().NotContain("tempId");
        second.SessionSummary.Should().NotContain("```");
    }

    [Fact]
    public void RecordAssistantResponse_ShouldPersistRichPayload()
    {
        var service = new ConversationalFlowService(_tempRoot);

        var context = service.PrepareContext(new AiFlowGenerationRequest(
            "修复 JSON 输出",
            SessionId: "session-rich-payload"));

        service.RecordAssistantResponse(
            context.SessionId,
            "本轮生成未通过结构校验，已生成纠错草稿，请确认后手动发送。",
            null,
            payload: new ConversationTurnPayload
            {
                Kind = "assistant_failure",
                Status = AiFlowGenerationResult.FailureTypeManualRetryRequired,
                InteractionState = AiInteractionStates.ManualRetry,
                TurnIntent = AiTurnIntents.ManualRetryRepair,
                RouterConfidence = AiRouterConfidence.High,
                BlockingClarificationFields = ["object_type", "object_type"],
                NonBlockingMissingFields = ["model_path", "roi", "model_path"],
                Reply = "请确认后手动发送纠错草稿。",
                Reasoning = "模型输出缺少 ResultOutput 参数。",
                Progress = ["正在分析需求", "正在校验生成结果"],
                ClarificationRequired = true,
                RequirementBrief = new AiRequirementBrief
                {
                    ScenarioName = "缺陷检测",
                    ClarificationRequired = true,
                    MissingFacts = ["需要确认对象"]
                },
                Failure = new ConversationTurnFailurePayload
                {
                    Summary = "缺少关键参数",
                    FailureSummary = new AiFailureSummary
                    {
                        Category = "validation",
                        Code = "missing_parameter",
                        Message = "缺少关键参数",
                        RepairTarget = "补齐 ResultOutput 的输入参数",
                        LastOutputSummary = "最近一次输出缺少 ResultOutput 参数"
                    },
                    Diagnostics =
                    [
                        new AiAttemptDiagnostic
                        {
                            AttemptNumber = 1,
                            Stage = "validation",
                            Summary = "缺少关键参数"
                        }
                    ]
                },
                ManualRetry = new AiManualRetryInfo
                {
                    Required = true,
                    Stage = "validation",
                    Draft = "请仅补齐缺失参数后返回 JSON。",
                    Summary = "缺少关键参数",
                    RepairTarget = "补齐 ResultOutput 的输入参数",
                    LastOutputSummary = "最近一次输出缺少 ResultOutput 参数"
                }
            });

        var reloadedService = new ConversationalFlowService(_tempRoot);
        var session = reloadedService.GetSession(context.SessionId);

        session.Should().NotBeNull();
        var assistantTurn = session!.History.Last();
        assistantTurn.Payload.Should().NotBeNull();
        assistantTurn.Payload!.Kind.Should().Be("assistant_failure");
        assistantTurn.Payload.Status.Should().Be(AiFlowGenerationResult.FailureTypeManualRetryRequired);
        assistantTurn.Payload.InteractionState.Should().Be(AiInteractionStates.ManualRetry);
        assistantTurn.Payload.TurnIntent.Should().Be(AiTurnIntents.ManualRetryRepair);
        assistantTurn.Payload.RouterConfidence.Should().Be(AiRouterConfidence.High);
        assistantTurn.Payload.BlockingClarificationFields.Should().BeEquivalentTo(["object_type"]);
        assistantTurn.Payload.NonBlockingMissingFields.Should().BeEquivalentTo(["model_path", "roi"]);
        assistantTurn.Payload.Progress.Should().ContainInOrder("正在分析需求", "正在校验生成结果");
        assistantTurn.Payload.Failure.Should().NotBeNull();
        assistantTurn.Payload.Failure!.FailureSummary!.Code.Should().Be("missing_parameter");
        assistantTurn.Payload.ManualRetry.Should().NotBeNull();
        assistantTurn.Payload.ManualRetry!.Required.Should().BeTrue();
        assistantTurn.Payload.ManualRetry.Stage.Should().Be("validation");
        assistantTurn.Payload.ManualRetry.Draft.Should().Be("请仅补齐缺失参数后返回 JSON。");
        assistantTurn.Payload.ClarificationRequired.Should().BeTrue();
        assistantTurn.Payload.RequirementBrief.Should().NotBeNull();
        assistantTurn.Payload.RequirementBrief!.ScenarioName.Should().Be("缺陷检测");
    }

    [Fact]
    public void UpdateWorkspaceSnapshot_ShouldPersistPlanAndRunIdsAcrossRestart()
    {
        var service = new ConversationalFlowService(_tempRoot);

        var session = service.UpdateWorkspaceSnapshot("workspace-session", new VisionAgentWorkspaceSnapshotUpdate
        {
            LifecycleState = "plan_ready",
            PlanRunId = "plan-run-1",
            PlanRunStatus = "completed",
            RequirementMode = AiRequirementModes.Draft,
            PlanQuestionSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["defect_definition"] = "defer"
            },
            ConfirmedPlanAnswers =
            [
                new VisionAgentPlanAnswer
                {
                    QuestionId = "image_source",
                    Field = "image_source",
                    Value = "camera",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection
                }
            ],
            OptimisticPlanAnswers =
            [
                new VisionAgentPlanAnswer
                {
                    QuestionId = "acceptance_criteria",
                    Field = "acceptance_criteria",
                    Value = "scratch area above threshold is NG",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                }
            ],
            AnswerRevision = 7,
            ReadinessPreview = new VisionAgentBuildReadinessPreviewResult
            {
                PlanId = "plan-1",
                PlanHash = "sha256:plan",
                AnswerRevision = 7,
                BuildReadiness = new VisionAgentBuildReadinessSnapshot
                {
                    CanBuild = false,
                    RemainingFields = ["acceptance_criteria"]
                }
            },
            MissingResources =
            [
                new VisionAgentResourceRequirement
                {
                    CanonicalId = "resource:v1|model_resource|deeplearning#1|modelpath",
                    ResourceType = "model_resource",
                    ResourceName = "模型资源",
                    OperatorKey = "deeplearning#1",
                    ParameterName = "ModelPath",
                    Status = VisionAgentResourceStatuses.Pending
                }
            ],
            ResourceDecisions = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["resource:v1|model_resource|deeplearning#1|modelpath"] = JsonSerializer.SerializeToElement(new
                {
                    status = "deferred",
                    source = "user_deferred",
                    metadataOnly = true
                })
            },
            ResourceRevision = 3,
            WorkspaceViewMode = "build",
            PendingPlanSnapshot = new VisionAgentPlanModeResult
            {
                PlanId = "plan-1",
                PlanHash = "sha256:plan",
                Goal = "Detect scratches",
                CanBuild = true
            },
            UserTurnId = "plan:plan-run-1:user",
            UserMessage = "Detect scratches"
        });

        session.WorkspaceSnapshot.Should().NotBeNull();
        session.WorkspaceSnapshot!.Revision.Should().Be(1);

        var reloaded = new ConversationalFlowService(_tempRoot).GetSession("workspace-session");

        reloaded.Should().NotBeNull();
        reloaded!.History.Should().ContainSingle(turn => turn.TurnId == "plan:plan-run-1:user");
        reloaded.WorkspaceSnapshot.Should().NotBeNull();
        reloaded.WorkspaceSnapshot!.PlanRunId.Should().Be("plan-run-1");
        reloaded.WorkspaceSnapshot.RequirementMode.Should().Be(AiRequirementModes.Draft);
        reloaded.WorkspaceSnapshot.PlanQuestionSelections.Should().ContainKey("defect_definition");
        reloaded.WorkspaceSnapshot.PlanQuestionSelections["defect_definition"].Should().Be("defer");
        reloaded.WorkspaceSnapshot.ConfirmedPlanAnswers.Should().ContainSingle(answer => answer.Field == "image_source");
        reloaded.WorkspaceSnapshot.OptimisticPlanAnswers.Should().ContainSingle(answer => answer.Field == "acceptance_criteria");
        reloaded.WorkspaceSnapshot.AnswerRevision.Should().Be(7);
        reloaded.WorkspaceSnapshot.ReadinessPreview!.AnswerRevision.Should().Be(7);
        reloaded.WorkspaceSnapshot.MissingResources.Should().ContainSingle(resource => resource.CanonicalId == "resource:v1|model_resource|deeplearning#1|modelpath");
        reloaded.WorkspaceSnapshot.ResourceDecisions["resource:v1|model_resource|deeplearning#1|modelpath"].GetProperty("status").GetString()
            .Should().Be("deferred");
        reloaded.WorkspaceSnapshot.ResourceRevision.Should().Be(3);
        reloaded.WorkspaceSnapshot.WorkspaceViewMode.Should().Be("build");
        reloaded.WorkspaceSnapshot.PendingPlanSnapshot!.PlanId.Should().Be("plan-1");
    }

    [Fact]
    public void TryUpdateWorkspaceSnapshot_ShouldRejectStaleRevisionWithoutOverwritingLatest()
    {
        var service = new ConversationalFlowService(_tempRoot);
        var initial = service.UpdateWorkspaceSnapshot("revision-session", new VisionAgentWorkspaceSnapshotUpdate
        {
            LifecycleState = "plan_ready",
            RequirementMode = AiRequirementModes.Strict,
            PlanQuestionSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["q1"] = "camera"
            }
        });

        var accepted = service.TryUpdateWorkspaceSnapshot("revision-session", new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = initial.WorkspaceSnapshot!.Revision,
            ClientMutationId = "mutation-1",
            RequirementMode = AiRequirementModes.Draft,
            PlanQuestionSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["q1"] = "file"
            }
        });
        var rejected = service.TryUpdateWorkspaceSnapshot("revision-session", new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = initial.WorkspaceSnapshot.Revision,
            ClientMutationId = "mutation-stale",
            RequirementMode = AiRequirementModes.Strict,
            PlanQuestionSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["q1"] = "stale"
            }
        });

        accepted.Success.Should().BeTrue();
        accepted.Revision.Should().Be(initial.WorkspaceSnapshot.Revision + 1);
        rejected.Success.Should().BeFalse();
        rejected.Conflict.Should().BeTrue();
        rejected.ErrorCode.Should().Be("workspace_revision_conflict");
        rejected.Snapshot!.RequirementMode.Should().Be(AiRequirementModes.Draft);
        rejected.Snapshot.PlanQuestionSelections["q1"].Should().Be("file");
        service.GetSession("revision-session")!.WorkspaceSnapshot!.PlanQuestionSelections["q1"].Should().Be("file");
    }

    [Fact]
    public void TryUpdateWorkspaceSnapshot_WhenPrimaryStoreFails_ShouldNotAdvanceMemoryOrDiskRevision()
    {
        var service = new ConversationalFlowService(_tempRoot);
        var initial = service.UpdateWorkspaceSnapshot("primary-fail-session", new VisionAgentWorkspaceSnapshotUpdate
        {
            LifecycleState = "plan_ready",
            PlanQuestionSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["q1"] = "camera"
            }
        });

        service.PrimaryStoreWriteFaultInjector = () => throw new IOException("primary failed");
        var failed = service.TryUpdateWorkspaceSnapshot("primary-fail-session", new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = initial.WorkspaceSnapshot!.Revision,
            ClientMutationId = "primary-fail-mutation",
            PlanQuestionSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["q1"] = "file"
            }
        });

        failed.Success.Should().BeFalse();
        failed.PersistenceStatus.PrimaryStoreSaved.Should().BeFalse();
        var inMemory = service.GetSession("primary-fail-session")!.WorkspaceSnapshot!;
        inMemory.Revision.Should().Be(initial.WorkspaceSnapshot.Revision);
        inMemory.PlanQuestionSelections["q1"].Should().Be("camera");

        var reloaded = new ConversationalFlowService(_tempRoot)
            .GetSession("primary-fail-session")!
            .WorkspaceSnapshot!;
        reloaded.Revision.Should().Be(initial.WorkspaceSnapshot.Revision);
        reloaded.PlanQuestionSelections["q1"].Should().Be("camera");
    }

    [Fact]
    public void TryUpdateWorkspaceSnapshot_WithSameMutationIdAndPayload_ShouldReplayWithoutDuplicateRevision()
    {
        var service = new ConversationalFlowService(_tempRoot);

        var first = service.TryUpdateWorkspaceSnapshot("idempotent-session", new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = 0,
            ClientMutationId = "mutation-same",
            RequirementMode = AiRequirementModes.Draft,
            PlanQuestionSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["q1"] = "file"
            }
        });
        var replay = service.TryUpdateWorkspaceSnapshot("idempotent-session", new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = 0,
            ClientMutationId = "mutation-same",
            RequirementMode = AiRequirementModes.Draft,
            PlanQuestionSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["q1"] = "file"
            }
        });

        first.Success.Should().BeTrue();
        replay.Success.Should().BeTrue();
        replay.Revision.Should().Be(first.Revision);
        service.GetSession("idempotent-session")!.WorkspaceSnapshot!.Revision.Should().Be(1);
    }

    [Fact]
    public void TryUpdateWorkspaceSnapshot_WithSameMutationIdAndDifferentPayload_ShouldRejectConflict()
    {
        var service = new ConversationalFlowService(_tempRoot);
        service.TryUpdateWorkspaceSnapshot("mutation-conflict-session", new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = 0,
            ClientMutationId = "mutation-conflict",
            PlanQuestionSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["q1"] = "camera"
            }
        }).Success.Should().BeTrue();

        var conflict = service.TryUpdateWorkspaceSnapshot("mutation-conflict-session", new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = 1,
            ClientMutationId = "mutation-conflict",
            PlanQuestionSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["q1"] = "file"
            }
        });

        conflict.Success.Should().BeFalse();
        conflict.Conflict.Should().BeTrue();
        conflict.ErrorCode.Should().Be("workspace_mutation_id_conflict");
        service.GetSession("mutation-conflict-session")!.WorkspaceSnapshot!.PlanQuestionSelections["q1"]
            .Should().Be("camera");
    }

    [Fact]
    public async Task TryUpdateWorkspaceSnapshot_ForTwoSessionsCommittedConcurrently_ShouldNotOverwriteEitherSession()
    {
        var service = new ConversationalFlowService(_tempRoot);

        await Task.WhenAll(
            Task.Run(() => service.TryUpdateWorkspaceSnapshot("concurrent-a", new VisionAgentWorkspaceSnapshotUpdate
            {
                ExpectedRevision = 0,
                ClientMutationId = "mutation-a",
                PlanQuestionSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["qa"] = "camera"
                }
            }).Success.Should().BeTrue()),
            Task.Run(() => service.TryUpdateWorkspaceSnapshot("concurrent-b", new VisionAgentWorkspaceSnapshotUpdate
            {
                ExpectedRevision = 0,
                ClientMutationId = "mutation-b",
                PlanQuestionSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["qb"] = "file"
                }
            }).Success.Should().BeTrue()));

        var reloaded = new ConversationalFlowService(_tempRoot);
        reloaded.GetSession("concurrent-a")!.WorkspaceSnapshot!.PlanQuestionSelections["qa"].Should().Be("camera");
        reloaded.GetSession("concurrent-b")!.WorkspaceSnapshot!.PlanQuestionSelections["qb"].Should().Be("file");
    }

    [Fact]
    public void ProjectBuildTerminal_WhenFirstPersistenceFails_ShouldRetryWithOneAssistantTurn()
    {
        var service = new ConversationalFlowService(_tempRoot);
        var request = new VisionAgentTerminalProjectionRequest
        {
            SessionId = "terminal-retry-session",
            AssistantTurnId = "build:run-1:terminal:3:assistant",
            AssistantMessage = "Build 已失败。",
            WorkspaceUpdate = new VisionAgentWorkspaceSnapshotUpdate
            {
                LifecycleState = "build_failed",
                BuildRunId = "run-1",
                BuildRunStatus = "failed",
                BuildTerminalSequence = 3
            }
        };

        service.PrimaryStoreWriteFaultInjector = () => throw new IOException("primary failed");
        service.ProjectBuildTerminal(request).Success.Should().BeFalse();
        service.GetSession("terminal-retry-session").Should().BeNull();

        service.PrimaryStoreWriteFaultInjector = null;
        var success = service.ProjectBuildTerminal(request);
        var replay = service.ProjectBuildTerminal(request);

        success.Success.Should().BeTrue();
        replay.Success.Should().BeTrue();
        replay.Revision.Should().Be(success.Revision);
        var session = service.GetSession("terminal-retry-session")!;
        session.History.Should().ContainSingle(turn => turn.TurnId == request.AssistantTurnId);
        session.WorkspaceSnapshot!.Revision.Should().Be(1);
    }

    [Fact]
    public void LoadSessionsFromStore_WhenMainFileIsCorrupt_ShouldRecoverLastGood()
    {
        var service = new ConversationalFlowService(_tempRoot);
        service.UpdateWorkspaceSnapshot("recover-session", new VisionAgentWorkspaceSnapshotUpdate
        {
            LifecycleState = "plan_ready",
            PlanRunId = "plan-recover",
            UserTurnId = "plan:plan-recover:user",
            UserMessage = "recover me"
        });

        var storePath = Path.Combine(_tempRoot, "conversation_sessions.json");
        File.WriteAllText(storePath, "{ this is not json");

        var reloaded = new ConversationalFlowService(_tempRoot);
        var session = reloaded.GetSession("recover-session");

        session.Should().NotBeNull();
        session!.WorkspaceSnapshot!.PlanRunId.Should().Be("plan-recover");
        Directory.EnumerateFiles(_tempRoot, "conversation_sessions.json.corrupt-*")
            .Should()
            .ContainSingle();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
