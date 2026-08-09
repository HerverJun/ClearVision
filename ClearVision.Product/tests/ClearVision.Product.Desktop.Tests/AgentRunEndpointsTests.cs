using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Middleware;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Handoff;
using ClearVision.Product.Infrastructure.AI.Tools;
using ClearVision.Product.Infrastructure.Cameras;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]

public sealed class AgentRunEndpointsTests
{
    private static readonly JsonSerializerOptions CaseInsensitiveWebJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact(DisplayName = "POST AgentRun creates run and returns started plus brief events")]
    public async Task CreateRun_ShouldReturnInitialEvents()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            clientOperationId = Guid.NewGuid(),
            target = new { targetKind = "new" },
            description = "Detect scratches on a metal part",
            mode = "new",
            useVisionAgentGenerateFlow = true,
            agentGenerateFlowMode = "scripted"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = document.RootElement.GetProperty("runId").GetString();
        runId.Should().StartWith("ar_");
        document.RootElement.GetProperty("brief").GetString().Should().Contain("Detect scratches");
        document.RootElement.TryGetProperty("streamToken", out _).Should().BeFalse();
        document.RootElement.TryGetProperty("streamTokenExpiresInSeconds", out _).Should().BeFalse();
        document.RootElement.GetProperty("events").EnumerateArray()
            .Select(evt => evt.GetProperty("eventType").GetString())
            .Should()
            .Equal(AgentRunEventTypes.RunStarted, AgentRunEventTypes.AssistantBrief);
    }

    [Fact(DisplayName = "POST AgentRun rejects empty description")]
    public async Task CreateRun_ShouldRejectEmptyDescription()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            description = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "AgentRun terminal projector persists session once from terminal event")]
    public async Task GenerateFlowTerminal_ShouldProjectSessionOnceFromTerminalEvent()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var sessionId = "session-terminal-projector";

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            clientOperationId = Guid.NewGuid(),
            target = new { targetKind = "new" },
            description = "Detect scratches on a metal part",
            sessionId,
            mode = "new",
            useVisionAgentGenerateFlow = true
        });
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = document.RootElement.GetProperty("runId").GetString()!;

        await host.Generation.WaitForCallAsync();
        await host.WaitForTerminalAsync(runId);
        await host.WaitForSessionHistoryCountAsync(sessionId, 1);

        host.Generation.LastCommand!.PersistResult.Should().BeFalse();
        var replay = host.StreamService.Replay(runId)!;
        var terminal = replay.Events.Last(evt =>
            evt.EventType is AgentRunEventTypes.RunCompleted or
                AgentRunEventTypes.RunFailed or
                AgentRunEventTypes.RunCancelled);
        var session = host.ConversationService.GetSession(sessionId)!;
        session.History.Should().HaveCount(1);
        session.History[0].Payload!.Progress.Should()
            .Contain(BuildCommandTransports.AgentRun)
            .And.Contain($"agent_run:{runId}")
            .And.Contain($"terminal:{terminal.Sequence}");

        var duplicate = host.TerminalProjector.Project(new VisionAgentBuildTerminalProjection(
            runId,
            BuildCommandTransports.AgentRun,
            host.Generation.LastRequest!,
            AgentRunEndpointTestHost.SuccessResult(),
            terminal));

        duplicate.Should().BeFalse();
        host.ConversationService.GetSession(sessionId)!.History.Should().HaveCount(1);
    }

    [Fact(DisplayName = "POST Agent plan returns backend structured scenario-specific PlanModeResult")]
    public async Task CreatePlan_ShouldReturnScenarioSpecificStructuredPlan()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();

        using var scratchResponse = await host.Client.PostAsJsonAsync("/api/ai/agent-plan", new
        {
            description = "帮我做一个金属表面划痕检测流程",
            originalUserPrompt = "帮我做一个金属表面划痕检测流程",
            currentFlowSnapshot = "{\"operators\":[{\"id\":\"camera\"}]}",
            templateSelection = new
            {
                mode = "catalog_lock",
                templateId = "tmpl-scratch",
                scenarioKey = "scratch"
            },
            attachmentSummary = new
            {
                count = 1,
                resourceKinds = new[] { "sample_image_metadata" },
                pathsRedacted = true
            }
        });
        using var wireResponse = await host.Client.PostAsJsonAsync("/api/ai/agent-plan", new
        {
            description = "做一个线序检测流程",
            originalUserPrompt = "做一个线序检测流程"
        });

        scratchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        wireResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scratchDoc = JsonDocument.Parse(await scratchResponse.Content.ReadAsStringAsync());
        using var wireDoc = JsonDocument.Parse(await wireResponse.Content.ReadAsStringAsync());
        var scratchEnvelope = scratchDoc.RootElement;
        var wireEnvelope = wireDoc.RootElement;
        var scratch = scratchEnvelope.GetProperty("planResult");
        var wire = wireEnvelope.GetProperty("planResult");

        scratchEnvelope.GetProperty("sessionId").GetString().Should().NotBeNullOrWhiteSpace();
        scratchEnvelope.GetProperty("workspaceSnapshot").GetProperty("revision").GetInt64().Should().BeGreaterThan(0);
        scratchEnvelope.GetProperty("persistenceStatus").GetProperty("primaryStoreSaved").GetBoolean().Should().BeTrue();
        scratchEnvelope.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();

        scratch.GetProperty("intent").GetString().Should().Be("surface_defect");
        scratch.GetProperty("recommendedRoute").GetProperty("routeId").GetString().Should().Be("surface_defect_detection");
        scratch.GetProperty("contextSummary").GetProperty("hasCurrentFlow").GetBoolean().Should().BeTrue();
        scratch.GetProperty("planHash").GetString().Should().StartWith("sha256:");
        scratch.GetProperty("templateSelection").GetProperty("mode").GetString().Should().Be("catalog_lock");
        scratch.GetProperty("templateSelection").GetProperty("templateId").GetString().Should().Be("tmpl-scratch");
        scratch.GetProperty("templateSelection").GetProperty("scenarioKey").GetString().Should().Be("scratch");
        scratch.GetProperty("clarificationQuestions").EnumerateArray()
            .Select(question => question.GetProperty("id").GetString())
            .Should()
            .Contain("q_fallback_image_source")
            .And.Contain("q_fallback_acceptance_criteria")
            .And.NotContain("sequence_rule");
        scratch.GetProperty("clarificationQuestions").EnumerateArray()
            .Should()
            .OnlyContain(question => question.GetProperty("options").EnumerateArray()
                .Any(option =>
                    option.GetProperty("recommended").GetBoolean() &&
                    option.GetProperty("value").GetString()!.EndsWith("_pending", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(option.GetProperty("label").GetString()) &&
                    !string.IsNullOrWhiteSpace(option.GetProperty("description").GetString()) &&
                    !string.IsNullOrWhiteSpace(option.GetProperty("impact").GetString())));
        scratch.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
        var recovered = new ConversationalFlowService(Path.Combine(host.RootDirectory, "sessions"));
        var recoveredSession = recovered.GetSession(scratchEnvelope.GetProperty("sessionId").GetString()!);
        recoveredSession.Should().NotBeNull();
        recoveredSession!.History.Should().ContainSingle(turn => turn.Role == "user");
        recoveredSession.WorkspaceSnapshot.Should().NotBeNull();
        recoveredSession.WorkspaceSnapshot!.PendingPlanSnapshot.Should().NotBeNull();
        recoveredSession.WorkspaceSnapshot.PlanRunStatus.Should().Be(AgentRunEventStatuses.Completed);

        wire.GetProperty("intent").GetString().Should().Be("wire_sequence");
        wire.GetProperty("clarificationQuestions").EnumerateArray()
            .Select(question => question.GetProperty("id").GetString())
            .Should()
            .Contain("q_fallback_inspection_object")
            .And.NotContain("defect_definition");
        wire.GetProperty("clarificationQuestions").EnumerateArray()
            .Where(question => question.GetProperty("id").GetString()!.StartsWith("q_fallback_", StringComparison.Ordinal))
            .Should()
            .OnlyContain(question => question.GetProperty("options").EnumerateArray()
                .Any(option =>
                    option.GetProperty("recommended").GetBoolean() &&
                    !string.IsNullOrWhiteSpace(option.GetProperty("value").GetString()) &&
                    !string.IsNullOrWhiteSpace(option.GetProperty("label").GetString()) &&
                    !string.IsNullOrWhiteSpace(option.GetProperty("description").GetString()) &&
                    !string.IsNullOrWhiteSpace(option.GetProperty("impact").GetString())));
        var sequenceQuestion = wire.GetProperty("clarificationQuestions").EnumerateArray()
            .Single(question => question.GetProperty("id").GetString() == "sequence_rule");
        sequenceQuestion.GetProperty("options").EnumerateArray()
            .Single(option => option.GetProperty("recommended").GetBoolean())
            .GetProperty("value").GetString()
            .Should().Be("left_to_right");
        wire.GetProperty("clarificationQuestions").EnumerateArray()
            .Where(question => question.GetProperty("id").GetString() != "sequence_rule")
            .Should()
            .OnlyContain(question => question.GetProperty("options").EnumerateArray()
                .Any(option =>
                    option.GetProperty("recommended").GetBoolean() &&
                    option.GetProperty("value").GetString()!.EndsWith("_pending", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact(DisplayName = "POST Agent plan primary persistence failure returns 503 before planner executes")]
    public async Task CreatePlan_PrimaryPersistenceFailure_ShouldReturn503AndNotExecutePlanner()
    {
        var plannerCalled = false;
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: (_, baseline, _) =>
        {
            plannerCalled = true;
            return Task.FromResult(baseline);
        });
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = () => throw new IOException("primary failed");

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-plan", new VisionAgentPlanModeRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Description = "detect scratches on metal",
            OriginalUserPrompt = "detect scratches on metal",
            SessionId = "session-plan-create-primary-fail"
        });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("errorCode").GetString().Should().Be("session_persistence_failed");
        root.GetProperty("publicMessage").GetString().Should().Contain("模型规划未启动");
        root.TryGetProperty("persistenceStatus", out _).Should().BeFalse();
        root.TryGetProperty("metadataOnly", out _).Should().BeFalse();
        plannerCalled.Should().BeFalse();
        host.ConversationService.GetSession("session-plan-create-primary-fail").Should().BeNull();
    }

    [Fact(DisplayName = "POST Agent plan terminal persistence failure returns plan with warning")]
    public async Task CreatePlan_TerminalPersistenceFailure_ShouldReturnPlanWithWarning()
    {
        var plannerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePlanner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plannerCalled = false;
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: async (_, baseline, ct) =>
        {
            plannerCalled = true;
            plannerStarted.TrySetResult();
            await releasePlanner.Task.WaitAsync(ct);
            return baseline with
            {
                PlanSource = "planner",
                FallbackReason = string.Empty,
                Goal = "terminal warning plan"
            };
        });

        var responseTask = host.Client.PostAsJsonAsync("/api/ai/agent-plan", new VisionAgentPlanModeRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Description = "detect scratches on metal",
            OriginalUserPrompt = "detect scratches on metal",
            SessionId = "session-plan-create-terminal-fail"
        });
        await plannerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = () => throw new IOException("terminal primary failed");
        releasePlanner.SetResult();
        using var response = await responseTask;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("planResult").GetProperty("goal").GetString().Should().Be("terminal warning plan");
        root.GetProperty("persistenceStatus").GetProperty("primaryStoreSaved").GetBoolean().Should().BeFalse();
        root.GetProperty("persistenceWarning").GetProperty("code").GetString().Should().Be("primary_store_save_failed");
        root.GetProperty("workspaceSnapshot").GetProperty("lifecycleState").GetString().Should().Be("planning");
        plannerCalled.Should().BeTrue();
        host.ConversationService.GetSession("session-plan-create-terminal-fail")!
            .WorkspaceSnapshot!
            .PendingPlanSnapshot
            .Should()
            .BeNull();
    }

    [Fact(DisplayName = "POST Agent plan readiness preview uses owner-bound canonical snapshot without creating AgentRun")]
    public async Task PreviewPlanReadiness_ShouldUseCanonicalSessionSnapshotWithoutCreatingRun()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var plan = LegacyBlockedAgentRunBuildFromPlanSnapshot();
        using var create = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId = Guid.NewGuid() });
        using var createDocument = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var sessionId = createDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString()!;
        var seeded = host.ConversationService.TryUpdateWorkspaceSnapshot(sessionId, new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = 0,
            ClientMutationId = Guid.NewGuid().ToString("D"),
            PendingPlanSnapshot = plan,
            ConfirmedPlanAnswers = ConfirmedAgentRunBuildFromPlanAnswers(),
            PlanQuestionSelections = new Dictionary<string, string> { ["defect_definition"] = "scratch_or_blob" },
            AnswerRevision = 12,
            ResourceRevision = 4,
            RequirementMode = AiRequirementModes.Strict
        });
        seeded.Success.Should().BeTrue();

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-plan/readiness-preview", new
        {
            sessionId,
            expectedRevision = seeded.Revision,
            planId = plan.PlanId,
            planHash = plan.PlanHash,
            planSnapshot = plan,
            requirementMode = AiRequirementModes.Strict,
            confirmedAnswers = ConfirmedAgentRunBuildFromPlanAnswers(),
            userSelections = new Dictionary<string, string>
            {
                ["defect_definition"] = "scratch_or_blob"
            },
            acceptedDefaults = new[] { "resource_policy" },
            acceptedRecommendedDefaults = false,
            answerRevision = 12,
            resourceRevision = 4,
            metadataOnly = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("planId").GetString().Should().Be(plan.PlanId);
        root.GetProperty("planHash").GetString().Should().Be(plan.PlanHash);
        root.GetProperty("answerRevision").GetInt32().Should().Be(12);
        root.GetProperty("resourceRevision").GetInt32().Should().Be(4);
        root.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
        root.TryGetProperty("runId", out _).Should().BeFalse();
        host.StreamService.ReplayLatest(string.Empty).Should().BeNull();
        host.Generation.LastCommand.Should().BeNull();
        host.ConversationService.GetSession(sessionId).Should().NotBeNull();
    }

    [Fact(DisplayName = "POST Agent intent router returns public route decision")]
    public async Task CreateIntentRouter_ShouldReturnPublicRouteDecision()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(intentRouterHandler: (request, _) =>
        {
            request.Description.Should().Be("hi");
            return Task.FromResult(new VisionAgentIntentRouterResult
            {
                Intent = "casual_chat",
                Confidence = "high",
                ShouldOpenPlan = false,
                ShouldBuildDirectly = false,
                CanBuild = false,
                NeedsClarification = false,
                PublicReason = "这是普通寒暄，不需要进入规划。",
                AssistantReply = "我在。",
                RouterSource = "test_router",
                MetadataOnly = true
            });
        });

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-intent-router-runs", new
        {
            description = "hi",
            metadataOnly = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("intent").GetString().Should().Be("casual_chat");
        root.GetProperty("shouldOpenPlan").GetBoolean().Should().BeFalse();
        root.GetProperty("canBuild").GetBoolean().Should().BeFalse();
        root.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "GET planning deadline returns the versioned backend budget contract")]
    public async Task GetPlanningDeadline_ShouldReturnPublishedBudgetContract()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();

        using var response = await host.Client.GetAsync("/api/ai/vision-agent/planning-deadline");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("contractVersion").GetString().Should().Be("v1");
        root.GetProperty("totalBudgetMs").GetInt32().Should().Be(120_000);
        root.GetProperty("clientNetworkMarginMs").GetInt32().Should().Be(15_000);
        root.GetProperty("minimumRepairBudgetMs").GetInt32().Should().Be(5_000);
    }

    [Fact(DisplayName = "POST Agent intent router maps total budget exhaustion to explicit 504")]
    public async Task CreateIntentRouter_DeadlineExceeded_ShouldReturnGatewayTimeout()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(intentRouterHandler: (_, _) =>
            Task.FromException<VisionAgentIntentRouterResult>(
                new VisionAgentPlanningDeadlineExceededException("intent_router")));

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-intent-router-runs", new
        {
            description = "detect scratches",
            metadataOnly = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("errorCode").GetString().Should().Be("planning_deadline_exceeded");
        root.GetProperty("timeoutKind").GetString().Should().Be("total_budget_exceeded");
        root.GetProperty("stage").GetString().Should().Be("intent_router");
    }

    [Fact(DisplayName = "POST Agent plan maps total budget exhaustion to explicit 504")]
    public async Task CreatePlan_DeadlineExceeded_ShouldReturnGatewayTimeout()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: (_, _, _) =>
            Task.FromException<VisionAgentPlanModeResult>(
                new VisionAgentPlanningDeadlineExceededException("plan_orchestration")));

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-plan", new VisionAgentPlanModeRequest
        {
            Description = "detect scratches",
            OriginalUserPrompt = "detect scratches",
            SessionId = "session-plan-deadline"
        });

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("errorCode").GetString().Should().Be("planning_deadline_exceeded");
        root.GetProperty("timeoutKind").GetString().Should().Be("total_budget_exceeded");
        root.GetProperty("stage").GetString().Should().Be("plan_orchestration");
    }

    [Fact(DisplayName = "POST Agent plan run streams public Plan events before completed")]
    public async Task CreatePlanRun_ShouldStreamPublicEventsBeforeCompleted()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: async (_, baseline, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return baseline with
            {
                PlanSource = "planner",
                FallbackReason = string.Empty,
                PublicEvents =
                [
                    PlanEvent("planning_with_model", "completed", "模型规划完成", "模型已返回公开结构化规划候选。"),
                    PlanEvent("validating_plan_contract", "completed", "规划契约已校验", "规划已归一到公开 PlanModeResult 契约。"),
                    PlanEvent("applying_safety_constraints", "completed", "安全约束已应用", "已应用安全约束。")
                ],
                MetadataOnly = true
            };
        });

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-plan-runs", new
        {
            clientOperationId = Guid.NewGuid(),
            description = "stream plan progress",
            originalUserPrompt = "stream plan progress",
            currentFlowSnapshot = "{\"operators\":[]}",
            attachmentSummary = new
            {
                count = 1,
                resourceKinds = new[] { "sample_image_metadata" },
                pathsRedacted = true
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var createDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = createDoc.RootElement.GetProperty("runId").GetString()!;

        await host.WaitForEventAsync(runId, AgentRunEventTypes.PlanModelStarted);
        var beforeCompleted = host.StreamService.Replay(runId)!;
        beforeCompleted.Events.Select(evt => evt.EventType).Should().Contain(new[]
        {
            AgentRunEventTypes.PlanContextStarted,
            AgentRunEventTypes.PlanContextCompleted,
            AgentRunEventTypes.PlanModelStarted
        });
        beforeCompleted.Events.Should().NotContain(evt => evt.EventType == AgentRunEventTypes.RunCompleted);

        gate.SetResult();
        await host.WaitForTerminalAsync(runId);
        var replay = host.StreamService.Replay(runId)!;

        replay.Events.Select(evt => evt.EventType).Should().Contain(new[]
        {
            AgentRunEventTypes.PlanModelCompleted,
            AgentRunEventTypes.PlanContractCompleted,
            AgentRunEventTypes.PlanSafetyCompleted,
            AgentRunEventTypes.PlanCompleted,
            AgentRunEventTypes.RunCompleted
        });
        var completed = replay.Events.Single(evt => evt.EventType == AgentRunEventTypes.PlanCompleted);
        using var completedDoc = JsonDocument.Parse(JsonSerializer.Serialize(completed, AgentRunEventJson.Options));
        completedDoc.RootElement.GetProperty("payload").GetProperty("planResult").GetProperty("planSource").GetString()
            .Should().Be("planner");
        completedDoc.RootElement.GetProperty("payload").GetProperty("planResult").GetProperty("planHash").GetString()
            .Should().StartWith("sha256:");
    }

    [Fact(DisplayName = "POST Agent plan run reuses provided semantic extraction without duplicate semantic events")]
    public async Task CreatePlanRun_WithSemanticExtraction_ShouldReuseSemanticAndAvoidDuplicateSemanticEvents()
    {
        var semantic = new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = AiVisionTaskTypes.AttributeClassification,
            Confidence = 0.92,
            TaskTypeConfidence = 0.9,
            InspectionObject = "strawberry",
            TargetAttribute = "maturity",
            ImageSource = "camera",
            OkCondition = "ripe is OK",
            NgCondition = "otherwise NG",
            SuggestedRoute = "attribute classification OK/NG route",
            Source = VisionAgentSemanticSources.Model,
            MetadataOnly = true
        };
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: (request, baseline, _) =>
        {
            request.SemanticExtraction.Should().NotBeNull();
            request.SemanticExtraction!.TaskType.Should().Be(AiVisionTaskTypes.AttributeClassification);
            return Task.FromResult(baseline with
            {
                PlanSource = "planner",
                FallbackReason = string.Empty,
                SemanticExtraction = request.SemanticExtraction,
                PublicEvents =
                [
                    PlanEvent("semantic_extraction", "completed", "Semantic extraction completed",
                        "Provided semantic extraction was reused.",
                        new(StringComparer.OrdinalIgnoreCase)
                        {
                            ["semanticSource"] = "model",
                            ["taskType"] = AiVisionTaskTypes.AttributeClassification,
                            ["inspectionObject"] = "strawberry",
                            ["targetAttribute"] = "maturity",
                            ["okCondition"] = "ripe is OK",
                            ["ngCondition"] = "otherwise NG",
                            ["imageSource"] = "camera"
                        }),
                    PlanEvent("semantic_extraction", "completed", "Duplicate semantic extraction completed",
                        "Duplicate semantic event should be dropped.")
                ],
                MetadataOnly = true
            });
        });

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-plan-runs", new VisionAgentPlanModeRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Description = "classify strawberry maturity",
            OriginalUserPrompt = "classify strawberry maturity",
            SemanticExtraction = semantic
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var createDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = createDoc.RootElement.GetProperty("runId").GetString()!;

        await host.WaitForTerminalAsync(runId);
        var replay = host.StreamService.Replay(runId)!;
        replay.Events.Count(evt => evt.EventType == AgentRunEventTypes.SemanticStarted).Should().Be(0);
        replay.Events.Count(evt => evt.EventType == AgentRunEventTypes.SemanticCompleted).Should().Be(1);
        replay.Events
            .Where(evt => evt.EventType is AgentRunEventTypes.SemanticStarted or AgentRunEventTypes.SemanticCompleted)
            .Should()
            .AllSatisfy(evt =>
            {
                evt.MetadataOnly.Should().BeTrue();
                evt.RedactionPass.Should().BeTrue();
            });

        var completed = replay.Events.Single(evt => evt.EventType == AgentRunEventTypes.PlanCompleted);
        using var completedDoc = JsonDocument.Parse(JsonSerializer.Serialize(completed, AgentRunEventJson.Options));
        var planSemantic = completedDoc.RootElement
            .GetProperty("payload")
            .GetProperty("planResult")
            .GetProperty("semanticExtraction");
        planSemantic.GetProperty("source").GetString().Should().Be(VisionAgentSemanticSources.Model);
        planSemantic.GetProperty("taskType").GetString().Should().Be(AiVisionTaskTypes.AttributeClassification);
        planSemantic.GetProperty("inspectionObject").GetString().Should().Be("strawberry");
        planSemantic.GetProperty("targetAttribute").GetString().Should().Be("maturity");
        planSemantic.GetProperty("okCondition").GetString().Should().Be("ripe is OK");
        planSemantic.GetProperty("ngCondition").GetString().Should().Be("otherwise NG");
        planSemantic.GetProperty("imageSource").GetString().Should().Be("camera");
    }

    [Fact(DisplayName = "POST PlanRun creates canonical ConversationSession workspace snapshot")]
    public async Task CreatePlanRun_ShouldCreateConversationSessionWorkspaceSnapshot()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-plan-runs", new VisionAgentPlanModeRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Description = "detect scratches on metal",
            OriginalUserPrompt = "detect scratches on metal",
            RequirementMode = AiRequirementModes.Draft
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var createDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = createDoc.RootElement.GetProperty("runId").GetString();
        var sessionId = createDoc.RootElement.GetProperty("sessionId").GetString();
        runId.Should().NotBeNullOrWhiteSpace();
        sessionId.Should().NotBeNullOrWhiteSpace();

        await host.WaitForTerminalAsync(runId!);

        host.ConversationService.ListSessions()
            .Should()
            .Contain(summary => summary.SessionId == sessionId);
        var session = host.ConversationService.GetSession(sessionId!);
        session.Should().NotBeNull();
        session!.History.Should().ContainSingle(turn =>
            turn.Role == "user" &&
            turn.TurnId == $"plan:{runId}:user" &&
            turn.Message == "detect scratches on metal");
        session.WorkspaceSnapshot.Should().NotBeNull();
        session.WorkspaceSnapshot!.PlanRunId.Should().Be(runId);
        session.WorkspaceSnapshot.PlanRunStatus.Should().Be(AgentRunEventStatuses.Completed);
        session.WorkspaceSnapshot.RequirementMode.Should().Be(AiRequirementModes.Draft);
        session.WorkspaceSnapshot.PendingPlanSnapshot.Should().NotBeNull();
        session.WorkspaceSnapshot.PendingPlanSnapshot!.MetadataOnly.Should().BeTrue();

        var completed = host.StreamService.Replay(runId!)!.Events.Last(evt => evt.EventType == AgentRunEventTypes.RunCompleted);
        using var completedPayload = JsonDocument.Parse(JsonSerializer.Serialize(completed.Payload, AgentRunEventJson.Options));
        var payload = completedPayload.RootElement;
        payload.GetProperty("planResult").GetProperty("planId").GetString().Should().NotBeNullOrWhiteSpace();
        payload.GetProperty("workspaceSnapshot").GetProperty("revision").GetInt64()
            .Should()
            .Be(session.WorkspaceSnapshot.Revision);
        payload.GetProperty("persistenceStatus").GetProperty("primaryStoreSaved").GetBoolean().Should().BeTrue();
        payload.TryGetProperty("persistenceWarning", out var warning).Should().BeTrue();
        warning.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "POST PlanRun primary persistence failure returns 503 before planner executes")]
    public async Task CreatePlanRun_PrimaryPersistenceFailure_ShouldFailRunAndNotExecutePlanner()
    {
        var plannerCalled = false;
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: (_, baseline, _) =>
        {
            plannerCalled = true;
            return Task.FromResult(baseline);
        });
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = () => throw new IOException("primary failed");

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-plan-runs", new VisionAgentPlanModeRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Description = "detect scratches on metal",
            OriginalUserPrompt = "detect scratches on metal",
            SessionId = "session-plan-primary-fail"
        });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("errorCode").GetString().Should().Be("session_persistence_failed");
        root.GetProperty("publicMessage").GetString().Should().Contain("模型规划未启动");
        root.TryGetProperty("runId", out _).Should().BeFalse();
        root.GetProperty("operation").GetProperty("status").GetString().Should().Be(AiOperationStatuses.Failed);
        plannerCalled.Should().BeFalse();
        host.StreamService.ReplayLatest(ResolveOwnerHashForTest("user-default")).Should().BeNull();
        host.ConversationService.GetSession("session-plan-primary-fail").Should().BeNull();
    }

    [Fact(DisplayName = "PlanRun terminal persistence failure emits explicit warning")]
    public async Task CreatePlanRun_TerminalPersistenceFailure_ShouldEmitWarning()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: async (_, baseline, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return baseline with { PlanSource = "planner", FallbackReason = string.Empty };
        });

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-plan-runs", new VisionAgentPlanModeRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Description = "detect scratches on metal",
            OriginalUserPrompt = "detect scratches on metal",
            SessionId = "session-plan-terminal-fail"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var createDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = createDoc.RootElement.GetProperty("runId").GetString()!;

        await host.WaitForEventAsync(runId, AgentRunEventTypes.PlanModelStarted);
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = () => throw new IOException("terminal primary failed");
        gate.SetResult();
        await host.WaitForTerminalAsync(runId);

        var replay = host.StreamService.Replay(runId)!;
        replay.Events.Should().Contain(evt =>
            evt.Stage == "workspace_persistence" &&
            evt.Status == AgentRunEventStatuses.Warning);
        var completed = replay.Events.Last(evt => evt.EventType == AgentRunEventTypes.RunCompleted);
        var completedJson = JsonSerializer.Serialize(completed, AgentRunEventJson.Options);
        completedJson.Should().Contain("\"persistenceWarning\"");
        completedJson.Should().Contain("primary_store_save_failed");
        using var completedPayload = JsonDocument.Parse(JsonSerializer.Serialize(completed.Payload, AgentRunEventJson.Options));
        var payload = completedPayload.RootElement;
        payload.GetProperty("planResult").GetProperty("planId").GetString().Should().NotBeNullOrWhiteSpace();
        payload.GetProperty("workspaceSnapshot").GetProperty("planRunStatus").GetString()
            .Should()
            .Be(AgentRunEventStatuses.Running);
        payload.GetProperty("persistenceStatus").GetProperty("primaryStoreSaved").GetBoolean().Should().BeFalse();
        host.ConversationService.GetSession("session-plan-terminal-fail")!
            .WorkspaceSnapshot!
            .PlanRunStatus
            .Should().Be(AgentRunEventStatuses.Running);
    }

    [Fact(DisplayName = "Plan run timeout emits timeout and fallback before completed PlanResult")]
    public async Task CreatePlanRun_ShouldEmitTimeoutFallbackBeforeCompleted()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: (_, baseline, _) =>
        {
            var fallback = baseline with
            {
                PlanSource = "rule_fallback",
                FallbackReason = "planner_timeout",
                PublicEvents =
                [
                    PlanEvent("planning_with_model", "failed", "模型规划超时", "模型规划超时，已使用规则兜底方案。",
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["fallbackReason"] = "planner_timeout"
                        }),
                    PlanEvent("rule_fallback_used", "completed", "已使用规则兜底方案", "模型规划超时，已使用规则兜底方案。",
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["fallbackReason"] = "planner_timeout"
                        })
                ],
                MetadataOnly = true
            };
            return Task.FromResult(fallback with
            {
                PlanHash = VisionAgentOrchestrator.ComputePlanHash(fallback)
            });
        });

        var runId = await host.CreatePlanRunAsync("timeout plan");
        await host.WaitForTerminalAsync(runId);
        var replay = host.StreamService.Replay(runId)!;
        var eventTypes = replay.Events.Select(evt => evt.EventType).ToList();

        eventTypes.Should().Contain(AgentRunEventTypes.PlanModelTimeout);
        eventTypes.Should().Contain(AgentRunEventTypes.PlanFallbackUsed);
        eventTypes.Should().Contain(AgentRunEventTypes.PlanCompleted);
        eventTypes.IndexOf(AgentRunEventTypes.PlanModelTimeout)
            .Should().BeLessThan(eventTypes.IndexOf(AgentRunEventTypes.PlanFallbackUsed));
        eventTypes.IndexOf(AgentRunEventTypes.PlanFallbackUsed)
            .Should().BeLessThan(eventTypes.IndexOf(AgentRunEventTypes.RunCompleted));

        var completed = replay.Events.Single(evt => evt.EventType == AgentRunEventTypes.PlanCompleted);
        var completedJson = JsonSerializer.Serialize(completed, AgentRunEventJson.Options);
        completedJson.Should().Contain("\"planSource\":\"rule_fallback\"");
        completedJson.Should().Contain("\"fallbackReason\":\"planner_timeout\"");
    }

    [Fact(DisplayName = "Plan run cancel closes stream without completed result")]
    public async Task CreatePlanRunCancel_ShouldNotPublishCompleted()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: async (_, baseline, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return baseline with { PlanSource = "planner" };
        });
        var runId = await host.CreatePlanRunAsync("cancel plan");
        await host.WaitForEventAsync(runId, AgentRunEventTypes.PlanModelStarted);

        using var cancel = await host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        gate.TrySetResult();
        await host.WaitForTerminalAsync(runId);

        var replay = host.StreamService.Replay(runId)!;
        replay.Events.Select(evt => evt.EventType).Should().Contain(AgentRunEventTypes.PlanCancelled);
        replay.Events.Should().Contain(evt => evt.EventType == AgentRunEventTypes.RunCancelled);
        replay.Events.Should().NotContain(evt => evt.EventType == AgentRunEventTypes.PlanCompleted);
        replay.Events.Should().NotContain(evt => evt.EventType == AgentRunEventTypes.RunCompleted);
        var runCancelled = replay.Events.Last(evt => evt.EventType == AgentRunEventTypes.RunCancelled);
        using var payloadDoc = JsonDocument.Parse(JsonSerializer.Serialize(runCancelled.Payload, AgentRunEventJson.Options));
        var payload = payloadDoc.RootElement;
        payload.GetProperty("workspaceSnapshot").GetProperty("planRunStatus").GetString()
            .Should()
            .Be(AgentRunEventStatuses.Cancelled);
        payload.GetProperty("persistenceStatus").GetProperty("primaryStoreSaved").GetBoolean().Should().BeTrue();
        payload.GetProperty("publicMessage").GetString().Should().Contain("规划已取消");
    }

    [Fact(DisplayName = "Plan run cancel endpoint remains authoritative when planner returns after cancel")]
    public async Task CreatePlanRunCancel_WhenPlannerReturnsAfterEndpointCancel_ShouldNotPersistTwice()
    {
        var releasePlanner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plannerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: async (_, baseline, _) =>
        {
            try
            {
                await releasePlanner.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return baseline with { PlanSource = "planner" };
            }
            finally
            {
                plannerExited.TrySetResult();
            }
        });
        var sessionId = "session-plan-cancel-return";
        var runId = await host.CreatePlanRunAsync("cancel plan then return", sessionId);
        await host.WaitForEventAsync(runId, AgentRunEventTypes.PlanModelStarted);

        using var cancel = await host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        releasePlanner.SetResult();
        await plannerExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForPlanRunBackgroundSettleAsync(host, runId, sessionId);

        AssertCancelledPlanRunTerminalConsistency(host, runId, sessionId, expectPrimarySaved: true);
    }

    [Fact(DisplayName = "Plan run cancel endpoint remains authoritative when planner later throws cancellation")]
    public async Task CreatePlanRunCancel_WhenPlannerThrowsCancellationAfterEndpointCancel_ShouldNotPersistTwice()
    {
        var releasePlanner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plannerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: async (_, _, _) =>
        {
            try
            {
                await releasePlanner.Task.WaitAsync(TimeSpan.FromSeconds(5));
                throw new OperationCanceledException("planner observed cancellation after endpoint terminal");
            }
            finally
            {
                plannerExited.TrySetResult();
            }
        });
        var sessionId = "session-plan-cancel-throws";
        var runId = await host.CreatePlanRunAsync("cancel plan then throw", sessionId);
        await host.WaitForEventAsync(runId, AgentRunEventTypes.PlanModelStarted);

        using var cancel = await host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        releasePlanner.SetResult();
        await plannerExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForPlanRunBackgroundSettleAsync(host, runId, sessionId);

        AssertCancelledPlanRunTerminalConsistency(host, runId, sessionId, expectPrimarySaved: true);
    }

    [Fact(DisplayName = "Plan run background cancellation persists terminal when no endpoint terminal exists")]
    public async Task CreatePlanRunBackgroundCancellation_ShouldPersistTerminalWhenNoEndpointTerminalExists()
    {
        var plannerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: async (_, baseline, ct) =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return baseline with { PlanSource = "planner" };
            }
            finally
            {
                plannerExited.TrySetResult();
            }
        });
        var sessionId = "session-plan-background-cancel";
        var runId = await host.CreatePlanRunAsync("background cancel plan", sessionId);
        await host.WaitForEventAsync(runId, AgentRunEventTypes.PlanModelStarted);

        host.StreamService.TryCancelToken(runId).Should().BeTrue();
        await plannerExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForPlanRunBackgroundSettleAsync(host, runId, sessionId);

        AssertCancelledPlanRunTerminalConsistency(host, runId, sessionId, expectPrimarySaved: true);
    }

    [Fact(DisplayName = "Plan run cancel persistence failure is not overwritten by background retry")]
    public async Task CreatePlanRunCancel_WhenTerminalPersistenceFails_ShouldNotRetryFromBackground()
    {
        var releasePlanner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plannerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: async (_, baseline, _) =>
        {
            try
            {
                await releasePlanner.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return baseline with { PlanSource = "planner" };
            }
            finally
            {
                plannerExited.TrySetResult();
            }
        });
        var sessionId = "session-plan-cancel-persistence-fail";
        var runId = await host.CreatePlanRunAsync("cancel plan with persistence failure", sessionId);
        await host.WaitForEventAsync(runId, AgentRunEventTypes.PlanModelStarted);
        var failNextWrite = 1;
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = () =>
        {
            if (System.Threading.Interlocked.Exchange(ref failNextWrite, 0) == 1)
            {
                throw new IOException("cancel terminal primary failed");
            }
        };

        using var cancel = await host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        releasePlanner.SetResult();
        await plannerExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForPlanRunBackgroundSettleAsync(host, runId, sessionId);

        AssertCancelledPlanRunTerminalConsistency(host, runId, sessionId, expectPrimarySaved: false);
    }

    [Fact(DisplayName = "Plan run cancel owns terminal while planner returns during cancel persistence")]
    public async Task CreatePlanRunCancel_WhenPlannerReturnsDuringCancelPersistence_ShouldKeepCancelledTerminal()
    {
        var releasePlanner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plannerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: async (_, baseline, _) =>
        {
            try
            {
                await releasePlanner.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return baseline with { PlanSource = "planner" };
            }
            finally
            {
                plannerExited.TrySetResult();
            }
        });
        var sessionId = "session-plan-cancel-gated-return";
        var runId = await host.CreatePlanRunAsync("cancel owns while planner returns", sessionId);
        await host.WaitForEventAsync(runId, AgentRunEventTypes.PlanModelStarted);

        var cancelPersistenceEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCancelPersistence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateNextWrite = 1;
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = () =>
        {
            if (Interlocked.Exchange(ref gateNextWrite, 0) == 1)
            {
                cancelPersistenceEntered.TrySetResult();
                releaseCancelPersistence.Task.GetAwaiter().GetResult();
            }
        };

        var cancelTask = host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);
        await cancelPersistenceEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            releasePlanner.SetResult();
            await plannerExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseCancelPersistence.TrySetResult();
        }

        using var cancel = await cancelTask.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = null;
        await WaitForPlanRunBackgroundSettleAsync(host, runId, sessionId);

        AssertCancelledPlanRunTerminalConsistency(host, runId, sessionId, expectPrimarySaved: true);
        host.StreamService.Replay(runId)!.Events.Should().NotContain(evt => evt.Stage == "plan_ready");
    }

    [Fact(DisplayName = "Plan run completed terminal rejects late cancel without changing revision")]
    public async Task CreatePlanRunCancel_AfterCompleted_ShouldReturnConflictWithoutChangingSession()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: (_, baseline, _) =>
            Task.FromResult(baseline with { PlanSource = "planner", FallbackReason = string.Empty }));
        var sessionId = "session-plan-completed-late-cancel";
        var runId = await host.CreatePlanRunAsync("complete before late cancel", sessionId);
        await host.WaitForTerminalAsync(runId);
        await WaitForPlanRunBackgroundSettleAsync(host, runId, sessionId);

        var beforeReplay = host.StreamService.Replay(runId)!;
        var beforeRevision = host.ConversationService.GetSession(sessionId)!.WorkspaceSnapshot!.Revision;
        using var cancel = await host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);
        cancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var document = JsonDocument.Parse(await cancel.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("errorCode").GetString().Should().Be("run_already_terminal");
        document.RootElement.GetProperty("terminalStatus").GetString().Should().Be(AgentRunEventStatuses.Completed);
        await WaitForPlanRunBackgroundSettleAsync(host, runId, sessionId);

        var afterReplay = host.StreamService.Replay(runId)!;
        afterReplay.Events.Count.Should().Be(beforeReplay.Events.Count);
        afterReplay.Events.Count(evt => evt.EventType == AgentRunEventTypes.RunCompleted).Should().Be(1);
        afterReplay.Events.Should().NotContain(evt => evt.EventType == AgentRunEventTypes.RunCancelled);
        host.ConversationService.GetSession(sessionId)!.WorkspaceSnapshot!.Revision.Should().Be(beforeRevision);
        host.ConversationService.GetSession(sessionId)!.WorkspaceSnapshot!.PlanRunStatus.Should().Be(AgentRunEventStatuses.Completed);
    }

    [Fact(DisplayName = "Plan run failed terminal rejects late cancel without plan_cancelled write")]
    public async Task CreatePlanRunCancel_AfterFailed_ShouldReturnConflictWithoutWritingCancelledSession()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: (_, _, _) =>
            throw new InvalidOperationException("planner failed before cancel"));
        var sessionId = "session-plan-failed-late-cancel";
        var runId = await host.CreatePlanRunAsync("fail before late cancel", sessionId);
        await host.WaitForTerminalAsync(runId);
        await WaitForPlanRunBackgroundSettleAsync(host, runId, sessionId);

        var beforeReplay = host.StreamService.Replay(runId)!;
        var beforeRevision = host.ConversationService.GetSession(sessionId)!.WorkspaceSnapshot!.Revision;
        var writesAfterFailure = 0;
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = () => Interlocked.Increment(ref writesAfterFailure);

        using var cancel = await host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);
        cancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var document = JsonDocument.Parse(await cancel.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("terminalStatus").GetString().Should().Be(AgentRunEventStatuses.Failed);
        await WaitForPlanRunBackgroundSettleAsync(host, runId, sessionId);

        writesAfterFailure.Should().Be(0);
        var afterReplay = host.StreamService.Replay(runId)!;
        afterReplay.Events.Count.Should().Be(beforeReplay.Events.Count);
        afterReplay.Events.Count(evt => evt.EventType == AgentRunEventTypes.RunFailed).Should().Be(1);
        afterReplay.Events.Should().NotContain(evt => evt.EventType == AgentRunEventTypes.PlanCancelled);
        host.ConversationService.GetSession(sessionId)!.WorkspaceSnapshot!.Revision.Should().Be(beforeRevision);
        host.ConversationService.GetSession(sessionId)!.WorkspaceSnapshot!.PlanRunStatus.Should().Be(AgentRunEventStatuses.Failed);
    }

    [Fact(DisplayName = "Plan run repeated cancel is idempotent without events or revision change")]
    public async Task CreatePlanRunCancel_AfterCancelled_ShouldReturnOkWithoutChangingSession()
    {
        var plannerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: async (_, baseline, ct) =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return baseline with { PlanSource = "planner" };
            }
            finally
            {
                plannerExited.TrySetResult();
            }
        });
        var sessionId = "session-plan-repeat-cancel";
        var runId = await host.CreatePlanRunAsync("repeat cancel plan", sessionId);
        await host.WaitForEventAsync(runId, AgentRunEventTypes.PlanModelStarted);

        using var firstCancel = await host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);
        firstCancel.StatusCode.Should().Be(HttpStatusCode.OK);
        await plannerExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForPlanRunBackgroundSettleAsync(host, runId, sessionId);
        var beforeReplay = host.StreamService.Replay(runId)!;
        var beforeRevision = host.ConversationService.GetSession(sessionId)!.WorkspaceSnapshot!.Revision;
        var writesAfterFirstCancel = 0;
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = () => Interlocked.Increment(ref writesAfterFirstCancel);

        using var secondCancel = await host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);
        secondCancel.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await secondCancel.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("cancellationStatus").GetString().Should().Be(AgentRunEventStatuses.Cancelled);
        await WaitForPlanRunBackgroundSettleAsync(host, runId, sessionId);

        writesAfterFirstCancel.Should().Be(0);
        var afterReplay = host.StreamService.Replay(runId)!;
        afterReplay.Events.Count.Should().Be(beforeReplay.Events.Count);
        afterReplay.Events.Count(evt => evt.EventType == AgentRunEventTypes.RunCancelled).Should().Be(1);
        host.ConversationService.GetSession(sessionId)!.WorkspaceSnapshot!.Revision.Should().Be(beforeRevision);
        host.ConversationService.GetSession(sessionId)!.WorkspaceSnapshot!.PlanRunStatus.Should().Be(AgentRunEventStatuses.Cancelled);
    }

    [Fact(DisplayName = "Plan run endpoint cancel and planner OperationCanceledException race to one cancelled terminal")]
    public async Task CreatePlanRunCancel_WhenPlannerThrowsCancellationDuringCancelPersistence_ShouldCommitOnce()
    {
        var releasePlanner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plannerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: async (_, _, _) =>
        {
            try
            {
                await releasePlanner.Task.WaitAsync(TimeSpan.FromSeconds(5));
                throw new OperationCanceledException("planner cancellation raced endpoint cancel");
            }
            finally
            {
                plannerExited.TrySetResult();
            }
        });
        var sessionId = "session-plan-cancel-oce-race";
        var runId = await host.CreatePlanRunAsync("cancel races planner cancellation", sessionId);
        await host.WaitForEventAsync(runId, AgentRunEventTypes.PlanModelStarted);

        var cancelPersistenceEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCancelPersistence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateNextWrite = 1;
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = () =>
        {
            if (Interlocked.Exchange(ref gateNextWrite, 0) == 1)
            {
                cancelPersistenceEntered.TrySetResult();
                releaseCancelPersistence.Task.GetAwaiter().GetResult();
            }
        };

        var cancelTask = host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);
        await cancelPersistenceEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            releasePlanner.SetResult();
            await plannerExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseCancelPersistence.TrySetResult();
        }

        using var cancel = await cancelTask.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = null;
        await WaitForPlanRunBackgroundSettleAsync(host, runId, sessionId);

        AssertCancelledPlanRunTerminalConsistency(host, runId, sessionId, expectPrimarySaved: true);
        host.StreamService.Replay(runId)!.Events.Count(evt => evt.EventType == AgentRunEventTypes.PlanCancelled).Should().Be(1);
    }

    [Fact(DisplayName = "Plan run cancel persistence failure remains cancelled while planner returns")]
    public async Task CreatePlanRunCancel_WhenPersistenceFailsAndPlannerReturns_ShouldKeepWarningAndAvoidCompletedOverwrite()
    {
        var releasePlanner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plannerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: async (_, baseline, _) =>
        {
            try
            {
                await releasePlanner.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return baseline with { PlanSource = "planner" };
            }
            finally
            {
                plannerExited.TrySetResult();
            }
        });
        var sessionId = "session-plan-cancel-fail-gated-return";
        var runId = await host.CreatePlanRunAsync("cancel persistence fails while planner returns", sessionId);
        await host.WaitForEventAsync(runId, AgentRunEventTypes.PlanModelStarted);

        var cancelPersistenceEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCancelPersistence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failNextWrite = 1;
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = () =>
        {
            if (Interlocked.Exchange(ref failNextWrite, 0) == 1)
            {
                cancelPersistenceEntered.TrySetResult();
                releaseCancelPersistence.Task.GetAwaiter().GetResult();
                throw new IOException("cancel terminal primary failed");
            }
        };

        var cancelTask = host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);
        await cancelPersistenceEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            releasePlanner.SetResult();
            await plannerExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseCancelPersistence.TrySetResult();
        }

        using var cancel = await cancelTask.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = null;
        await WaitForPlanRunBackgroundSettleAsync(host, runId, sessionId);

        AssertCancelledPlanRunTerminalConsistency(host, runId, sessionId, expectPrimarySaved: false);
        var replay = host.StreamService.Replay(runId)!;
        replay.Events.Should().NotContain(evt => evt.EventType == AgentRunEventTypes.RunCompleted);
        replay.Events.Should().Contain(evt =>
            evt.Stage == "workspace_persistence" &&
            evt.Status == AgentRunEventStatuses.Warning);
    }

    [Fact(DisplayName = "Plan run failure terminal payload carries final workspace and persistence status")]
    public async Task CreatePlanRunFailure_ShouldPublishFinalWorkspaceAndPersistenceStatus()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: (_, _, _) =>
            throw new InvalidOperationException("planner exploded"));

        var runId = await host.CreatePlanRunAsync("failing plan");
        await host.WaitForTerminalAsync(runId);

        var replay = host.StreamService.Replay(runId)!;
        replay.Events.Should().Contain(evt => evt.EventType == AgentRunEventTypes.PlanFailed);
        var runFailed = replay.Events.Last(evt => evt.EventType == AgentRunEventTypes.RunFailed);
        using var payloadDoc = JsonDocument.Parse(JsonSerializer.Serialize(runFailed.Payload, AgentRunEventJson.Options));
        var payload = payloadDoc.RootElement;
        payload.GetProperty("workspaceSnapshot").GetProperty("planRunStatus").GetString()
            .Should()
            .Be(AgentRunEventStatuses.Failed);
        payload.GetProperty("persistenceStatus").GetProperty("primaryStoreSaved").GetBoolean().Should().BeTrue();
        payload.GetProperty("publicMessage").GetString().Should().Contain("规划在完成前失败");
        payload.GetProperty("diagnostic").GetProperty("workspaceSnapshot").GetProperty("planRunStatus").GetString()
            .Should()
            .Be(AgentRunEventStatuses.Failed);
    }

    [Fact(DisplayName = "Plan run replay carries redacted PlanResult without private reasoning or raw prompts")]
    public async Task CreatePlanRunReplay_ShouldRedactPrivatePlanPayload()
    {
        var syntheticImageDataUri = "data:image/png;" + "base64," + new string('A', 96);

        await using var host = await AgentRunEndpointTestHost.CreateAsync(planHandler: (_, baseline, _) =>
        {
            var plan = baseline with
            {
                PlanSource = "planner",
                OriginalUserPrompt = @"inspect C:\factory\secret.png token=abc123",
                SanitizedErrorMessage = "Semantic extraction model authorization failed; rule fallback is active.",
                SemanticExtraction = new VisionAgentSemanticExtractionResult
                {
                    IsVisionRequest = true,
                    Intent = "new_flow",
                    TaskType = AiVisionTaskTypes.SurfaceDefect,
                    SanitizedErrorMessage = "Semantic extraction model authorization failed; rule fallback is active.",
                    MetadataOnly = true
                },
                PlanWarnings =
                [
                    "rawPrompt=hidden prompt",
                    "systemPrompt=hidden system",
                    "reasoning_content: private trace",
                    @"model path C:\factory\models\secret.onnx",
                    $"station 192.168.10.45 DB1.DBW0 {syntheticImageDataUri}"
                ],
                DecisionTrace = new AiDecisionTrace
                {
                    RawUserText = @"inspect C:\factory\secret.png token=abc123",
                    TurnIntent = "create_plan",
                    InteractionState = "planning",
                    MetadataOnly = true
                },
                PublicEvents =
                [
                    PlanEvent("planning_with_model", "started", "模型规划已开始", "模型正在生成公开结构化规划候选。"),
                    PlanEvent("planning_with_model", "completed", "模型规划完成", "模型已返回公开结构化规划候选。")
                ],
                MetadataOnly = true
            };
            return Task.FromResult(plan with
            {
                PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
            });
        });

        using var sessionResponse = await host.Client.PostAsJsonAsync("/api/ai/sessions", new
        {
            clientOperationId = Guid.NewGuid()
        });
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var sessionDocument = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        var sessionId = sessionDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString()!;

        var runId = await host.CreatePlanRunAsync(@"inspect C:\factory\secret.png token=abc123", sessionId);
        await host.WaitForTerminalAsync(runId);

        using var response = await host.Client.GetAsync($"/api/ai/agent-runs/{runId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayJson = await response.Content.ReadAsStringAsync();

        replayJson.Should().Contain("\"planResult\"");
        replayJson.Should().Contain("\"decisionTrace\":null");
        replayJson.Should().NotContain("\"eventTypeRedacted\":true");
        replayJson.Should().NotContain("\"pendingPlanSnapshot\"");
        replayJson.ToLowerInvariant().Should().NotContain("authorization");
        replayJson.Should().NotContain("rawPrompt");
        replayJson.Should().NotContain("systemPrompt");
        replayJson.Should().NotContain("chainOfThought");
        replayJson.Should().NotContain("reasoning_content");
        replayJson.Should().NotContain(@"C:\factory");
        replayJson.Should().NotContain("192.168.10.45");
        replayJson.Should().NotContain("DB1.DBW0");
        replayJson.Should().NotContain("data:image/png;base64");
        replayJson.Should().NotContain("abc123");
        using var replayDocument = JsonDocument.Parse(replayJson);
        var startedEvent = replayDocument.RootElement.GetProperty("events").EnumerateArray()
            .Single(evt => evt.GetProperty("eventType").GetString() == AgentRunEventTypes.PlanModelStarted);
        startedEvent.GetProperty("status").GetString().Should().Be(AgentRunEventStatuses.Running);
        var completedEvent = replayDocument.RootElement.GetProperty("events").EnumerateArray()
            .Single(evt => evt.GetProperty("eventType").GetString() == AgentRunEventTypes.PlanCompleted);
        var publicPlan = JsonSerializer.Deserialize<VisionAgentPlanModeResult>(
            completedEvent.GetProperty("payload").GetProperty("planResult").GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        publicPlan.Should().NotBeNull();
        publicPlan!.PlanHash.Should().Be(VisionAgentOrchestrator.ComputePlanHash(publicPlan));
    }

    [Fact(DisplayName = "AgentRun background GenerateFlow receives safe metadata-only request")]
    public async Task CreateRun_ShouldPassSafeGenerationRequest()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            clientOperationId = Guid.NewGuid(),
            target = new { targetKind = "new" },
            description = "Modify current flow",
            additionalContext = "keep thresholds",
            sessionId = "session-1",
            existingFlowJson = "{\"operators\":[]}",
            attachments = new[] { @"C:\factory\image.png" },
            attachmentCount = 1,
            mode = "modify",
            runtimePreviewConsent = true,
            templateSelection = new
            {
                mode = "template_fill",
                templateId = "template-1",
                scenarioKey = "scratch"
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var createDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = createDoc.RootElement.GetProperty("runId").GetString()!;
        await host.Generation.WaitForCallAsync();

        var request = host.Generation.LastRequest!;
        request.AgentRunId.Should().StartWith("ar_");
        request.Description.Should().Be("Modify current flow");
        request.AdditionalContext.Should().Be("keep thresholds");
        request.SessionId.Should().Be("session-1");
        request.ExistingFlowJson.Should().Be("{\"operators\":[]}");
        request.Attachments.Should().BeEmpty();
        request.Mode.Should().Be(GenerateFlowMode.Modify);
        request.RuntimePreviewConsent.Should().BeTrue();
        request.UseVisionAgentGenerateFlow.Should().BeTrue();
        request.TemplateSelection.Should().NotBeNull();
        request.TemplateSelection!.Mode.Should().Be("template_fill");
        await host.WaitForTerminalAsync(runId);
    }

    [Fact(DisplayName = "POST AgentRun ad-hoc should reject same-session running build before background start")]
    public async Task CreateRun_AdHocSameSessionRunning_ShouldReturnConflictAndNotStartSecondBuild()
    {
        var firstBuildEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstBuild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(async (_, ct) =>
        {
            firstBuildEntered.TrySetResult();
            await releaseFirstBuild.Task.WaitAsync(ct);
            return AgentRunEndpointTestHost.SuccessResult();
        });
        var sessionId = "session-adhoc-running-conflict";

        using var firstResponse = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new AgentRunCreateRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Target = NewProjectTarget(),
            Description = "Detect scratches first",
            SessionId = sessionId,
            Mode = "new",
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = null
        });
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var firstDocument = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        var firstRunId = firstDocument.RootElement.GetProperty("runId").GetString()!;
        firstDocument.RootElement.GetProperty("workspaceSnapshot").GetProperty("buildRunStatus").GetString()
            .Should().Be(AgentRunEventStatuses.Running);
        await firstBuildEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var secondResponse = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new AgentRunCreateRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Target = NewProjectTarget(),
            Description = "Detect scratches second",
            SessionId = sessionId,
            Mode = "new",
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = null
        });

        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var secondDocument = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        var root = secondDocument.RootElement;
        root.GetProperty("errorCode").GetString().Should().Be("agent_run_already_running");
        root.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
        root.GetProperty("workspaceSnapshot").GetProperty("buildRunId").GetString().Should().Be(firstRunId);
        root.GetProperty("workspaceSnapshot").GetProperty("buildRunStatus").GetString()
            .Should().Be(AgentRunEventStatuses.Running);
        var secondRunId = root.GetProperty("runId").GetString()!;
        host.StreamService.Replay(secondRunId)!.Summary.Status.Should().Be(AgentRunEventStatuses.Failed);
        host.Generation.BuildCallCount.Should().Be(1);

        releaseFirstBuild.SetResult();
        await host.WaitForTerminalAsync(firstRunId);
        await host.WaitForWorkspaceBuildStatusAsync(sessionId, AgentRunEventStatuses.Completed);
        host.ConversationService.GetSession(sessionId)!.WorkspaceSnapshot!.BuildRunStatus
            .Should().Be(AgentRunEventStatuses.Completed);
    }

    [Fact(DisplayName = "POST AgentRun BuildFromPlan with old blocked Plan and confirmed answers completes through orchestrator")]
    public async Task CreateRun_BuildFromPlanWithOldBlockedPlanAndConfirmedAnswers_ShouldCompleteThroughOrchestrator()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync((request, _) =>
        {
            request.BuildFromPlan.Should().NotBeNull();
            request.BuildFromPlan!.PlanSnapshot.Should().NotBeNull();
            request.BuildFromPlan.PlanSnapshot!.CanBuild.Should().BeFalse();
            request.BuildFromPlan.ConfirmedAnswers.Select(answer => answer.Field)
                .Should()
                .Contain([
                    VisionAgentPlanAnswerFields.InspectionObject,
                    VisionAgentPlanAnswerFields.TaskType,
                    VisionAgentPlanAnswerFields.ImageSource,
                    VisionAgentPlanAnswerFields.AcceptanceCriteria
                ]);

            var result = AgentRunEndpointTestHost.SuccessResult();
            result.GenerationMode = "agent_run_build_from_plan_entry_reached";
            return Task.FromResult(result);
        });
        var plan = LegacyBlockedAgentRunBuildFromPlanSnapshot();

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new AgentRunCreateRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Target = NewProjectTarget(),
            Description = "start build from confirmed plan",
            Mode = "new",
            RequirementMode = AiRequirementModes.Strict,
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Scripted,
            BuildFromPlan = new VisionAgentBuildFromPlanRequest
            {
                PlanId = plan.PlanId,
                PlanHash = plan.PlanHash,
                PlanSnapshot = plan,
                ConfirmedAnswers = ConfirmedAgentRunBuildFromPlanAnswers(),
                OriginalUserPrompt = plan.OriginalUserPrompt,
                MetadataOnly = true
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var createDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = createDoc.RootElement.GetProperty("runId").GetString()!;
        await host.Generation.WaitForCallAsync();
        await host.WaitForTerminalAsync(runId);

        var replay = host.StreamService.Replay(runId)!;
        replay.Summary.Status.Should().Be(AgentRunEventStatuses.Completed);
        replay.Events.Should().Contain(evt => evt.EventType == AgentRunEventTypes.RunCompleted);
        replay.Events.Should().NotContain(evt => evt.EventType == AgentRunEventTypes.RunFailed);
        host.Generation.LastRequest!.BuildFromPlan!.ConfirmedAnswers.Should().HaveCount(4);
        host.Generation.LastRequest.BuildFromPlan.PlanSnapshot!.CanBuild.Should().BeFalse();
    }

    [Fact(DisplayName = "AgentRun create rejects tool_loop GenerateFlow mode in production")]
    public async Task CreateRun_ShouldRejectToolLoopModeInProduction()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            clientOperationId = Guid.NewGuid(),
            target = new { targetKind = "new" },
            description = "Detect scratches on a metal part with Tool Loop experimental build",
            useVisionAgentGenerateFlow = true,
            agentGenerateFlowMode = "tool_loop"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var errorDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        errorDoc.RootElement.GetProperty("errorCode").GetString()
            .Should().Be(AiAgentGenerateFlowModePolicy.ToolLoopUnavailableCode);
        errorDoc.RootElement.GetProperty("effectiveMode").GetString()
            .Should().BeEmpty();
        host.Generation.LastRequest.Should().BeNull();
    }

    [Fact(DisplayName = "POST AgentRun preserves structured BuildFromPlan input and replays Plan Build payload")]
    public async Task CreateRun_ShouldPreserveBuildFromPlanContractAndReplayEvents()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        const string currentFlowSnapshot = "{\"operators\":[{\"id\":\"existing-camera\"}],\"connections\":[]}";

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            clientOperationId = Guid.NewGuid(),
            target = new { targetKind = "new" },
            description = "帮我做一个金属表面划痕检测流程",
            sessionId = "session-plan-build",
            templateSelection = new
            {
                mode = "template_fill",
                templateId = "top-level-template",
                scenarioKey = "top-level"
            },
            buildFromPlan = new
            {
                planId = "plan_scratch_1",
                planHash = "sha256:build-plan-hash",
                planSnapshot = new
                {
                    planId = "plan_scratch_1",
                    planHash = "sha256:build-plan-hash",
                    originalUserPrompt = "帮我做一个金属表面划痕检测流程",
                    goal = "金属表面划痕检测",
                    intent = "surface_defect",
                    confidence = "high",
                    requirementUnderstanding = new[] { "Inspection intent: surface defect inspection." },
                    recommendedRoute = new
                    {
                        routeId = "surface_defect_detection",
                        title = "Surface defect inspection route",
                        summary = "Enhance and segment scratch candidates.",
                        operators = new[] { "ImageAcquisition", "SurfaceDefectDetection", "BlobAnalysis", "ResultOutput" },
                        templateDecision = "catalog_match"
                    },
                    clarificationQuestions = new[]
                    {
                        new
                        {
                            id = "defect_definition",
                            title = "What should count as a defect?",
                            why = "Thresholds depend on defect definition.",
                            defaultValue = "scratch_or_blob",
                            defaultAssumption = "Detect visible scratches and blobs.",
                            impact = "Thresholds need sample confirmation.",
                            options = new[]
                            {
                                new
                                {
                                    value = "scratch_or_blob",
                                    label = "Scratch/blob",
                                    recommended = true,
                                    description = "Use visible surface defect candidates.",
                                    impact = "Good first draft."
                                }
                            }
                        }
                    },
                    recommendedDefaults = new[]
                    {
                        new
                        {
                            id = "resource_policy",
                            label = "Missing resources stay pending",
                            value = "pending_parameters",
                            impact = "No resource path is guessed."
                        }
                    },
                    risks = new[] { "Thresholds need representative images." },
                    acceptanceCriteria = new[] { "Workflow draft contains acquisition, inspection, judgment, and output stages." },
                    executablePlan = new[] { "Map parameters and run readiness checks." },
                    canBuild = true,
                    blockingReasons = Array.Empty<string>(),
                    nextAction = "Start Build.",
                    contextSummary = new
                    {
                        hasCurrentFlow = true,
                        hasCurrentResult = false,
                        attachmentCount = 2,
                        templateSelectionMode = "template_adapt",
                        templateId = "tmpl-scratch",
                        contextKinds = new[] { "user_requirement", "current_flow", "operator_catalog" },
                        operatorCatalogTools = new[] { "validate_flow" }
                    },
                    operatorCatalogVersion = "catalog.v1",
                    templateCatalogVersion = "template.v1",
                    templateSelection = new
                    {
                        mode = "template_adapt",
                        templateId = "tmpl-scratch",
                        scenarioKey = "scratch"
                    },
                    stationBoundarySummary = "metadata-only station boundary",
                    plcOutputPolicy = "local result first",
                    metadataOnly = true
                },
                userSelections = new Dictionary<string, string>
                {
                    ["defect_definition"] = "scratch_or_blob"
                },
                acceptedDefaults = new[] { "defect_definition", "resource_policy" },
                currentFlowSnapshot,
                templateSelection = new
                {
                    mode = "template_adapt",
                    templateId = "tmpl-scratch",
                    scenarioKey = "scratch"
                },
                attachmentSummary = new
                {
                    count = 2,
                    resourceKinds = new[] { "sample_image_metadata" },
                    pathsRedacted = true
                },
                operatorCatalogVersion = "catalog.v1",
                stationBoundarySummary = "metadata-only station boundary",
                plcOutputPolicy = "local result first",
                buildIntent = "modify",
                originalUserPrompt = "帮我做一个金属表面划痕检测流程",
                acceptedRecommendedDefaults = true,
                metadataOnly = true
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var createDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = createDoc.RootElement.GetProperty("runId").GetString()!;
        await host.Generation.WaitForCallAsync();
        await host.WaitForTerminalAsync(runId);

        var request = host.Generation.LastRequest!;
        request.BuildFromPlan.Should().NotBeNull();
        request.BuildFromPlan!.PlanId.Should().Be("plan_scratch_1");
        request.BuildFromPlan.PlanHash.Should().Be("sha256:build-plan-hash");
        request.BuildFromPlan.PlanSnapshot.Should().NotBeNull();
        request.BuildFromPlan.UserSelections.Should().ContainKey("defect_definition");
        request.BuildFromPlan.AcceptedDefaults.Should().Contain("resource_policy");
        request.ExistingFlowJson.Should().Be(currentFlowSnapshot);
        request.Mode.Should().Be(GenerateFlowMode.Modify);
        request.TemplateSelection.Should().NotBeNull();
        request.TemplateSelection!.TemplateId.Should().Be("tmpl-scratch");
        request.Attachments.Should().BeEmpty();
        host.Generation.LastCommand.Should().NotBeNull();
        host.Generation.LastCommand!.Transport.Should().Be(BuildCommandTransports.AgentRun);
        host.Generation.LastCommand.RunId.Should().Be(runId);
        host.Generation.LastCommand.PersistResult.Should().BeFalse();
        await host.WaitForSessionHistoryCountAsync("session-plan-build", 1);

        var replay = host.StreamService.Replay(runId)!;
        replay.Events.Select(evt => evt.Stage).Should().ContainInOrder([
            "canonical_build_contract",
            "canonical_build_readiness"
        ]);
        var completed = replay.Events.Single(evt => evt.EventType == AgentRunEventTypes.RunCompleted);
        var session = host.ConversationService.GetSession("session-plan-build")!;
        session.History.Should().HaveCount(1);
        session.History[0].Payload!.Progress.Should()
            .Contain(BuildCommandTransports.AgentRun)
            .And.Contain($"agent_run:{runId}")
            .And.Contain($"terminal:{completed.Sequence}");
        host.TerminalProjector.Project(new VisionAgentBuildTerminalProjection(
            runId,
            BuildCommandTransports.AgentRun,
            request,
            AgentRunEndpointTestHost.SuccessResult(),
            completed)).Should().BeFalse();
        session.History.Should().HaveCount(1);
        using var replayResponse = await host.Client.GetAsync($"/api/ai/agent-runs/{runId}");
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var replayDoc = JsonDocument.Parse(await replayResponse.Content.ReadAsStringAsync());
        replayDoc.RootElement.GetProperty("events").EnumerateArray()
            .Select(evt => evt.GetProperty("eventType").GetString())
            .Should()
            .Contain(AgentRunEventTypes.RunCompleted);
        var completedJson = JsonSerializer.Serialize(completed, AgentRunEventJson.Options);
        completedJson.Should().Contain("\"buildFromPlan\"");
        completedJson.Should().Contain("\"planId\":\"plan_scratch_1\"");
        completedJson.Should().Contain("\"planHash\":\"sha256:build-plan-hash\"");
        completedJson.Should().Contain("\"templateSelectionMode\":\"template_adapt\"");
        completedJson.Should().Contain("\"templateId\":\"tmpl-scratch\"");
        completedJson.Should().Contain("\"currentFlowSnapshotIncluded\":true");
        completedJson.Should().Contain("\"operatorCatalogVersion\":\"catalog.v1\"");
        completedJson.Should().NotContain("reasoning_content");
        completedJson.Should().NotContain("systemPrompt");
        completedJson.Should().NotContain("rawPrompt");
        completedJson.Should().NotContain("C:\\");
    }

    [Fact(DisplayName = "POST AgentRun BuildFromPlan primary persistence failure returns controlled failed run")]
    public async Task CreateRun_BuildFromPlanPrimaryPersistenceFailure_ShouldNotStartBackgroundRun()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = () => throw new IOException("primary failed");

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new AgentRunCreateRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Target = NewProjectTarget(),
            Description = "Build from persisted plan",
            SessionId = "session-build-primary-fail",
            Mode = "new",
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = BuildableAgentRunBuildFromPlanRequest()
        });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("errorCode").GetString().Should().Be("session_persistence_failed");
        root.TryGetProperty("runId", out _).Should().BeFalse();
        root.TryGetProperty("events", out _).Should().BeFalse();
        root.GetProperty("operation").GetProperty("status").GetString().Should().Be(AiOperationStatuses.Failed);
        host.Generation.LastCommand.Should().BeNull();
        host.StreamService.ReplayLatest(ResolveOwnerHashForTest("user-default")).Should().BeNull();
        host.ConversationService.GetSession("session-build-primary-fail").Should().BeNull();
    }

    [Fact(DisplayName = "POST AgentRun BuildFromPlan stale workspace revision returns controlled failed run")]
    public async Task CreateRun_BuildFromPlanStaleWorkspaceRevision_ShouldNotStartBackgroundRun()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var ownerHash = ResolveOwnerHashForTest("user-default");
        host.ConversationService.GetOrCreateOwnedSession(ownerHash, "session-build-stale-revision")
            .Status.Should().Be(ConversationOwnedSessionStatus.Ready);
        var initial = host.ConversationService.TryUpdateOwnedWorkspaceSnapshot(
            ownerHash,
            "session-build-stale-revision",
            new VisionAgentWorkspaceSnapshotUpdate
            {
                LifecycleState = "plan_ready",
                RequirementMode = AiRequirementModes.Strict
            });

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new AgentRunCreateRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Target = NewProjectTarget(),
            Description = "Build from stale persisted plan",
            SessionId = "session-build-stale-revision",
            Mode = "new",
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = BuildableAgentRunBuildFromPlanRequest() with
            {
                WorkspaceExpectedRevision = initial.Snapshot!.Revision - 1
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("errorCode").GetString().Should().Be("workspace_revision_conflict");
        root.GetProperty("workspaceSnapshot").GetProperty("revision").GetInt64()
            .Should().Be(initial.Snapshot.Revision);
        JsonSerializer.Serialize(root.GetProperty("events")).Should().Contain("workspace_revision_conflict");
        host.Generation.LastCommand.Should().BeNull();
        host.ConversationService.GetSession("session-build-stale-revision")!
            .WorkspaceSnapshot!
            .BuildRunId
            .Should().BeNull();
    }

    [Fact(DisplayName = "POST AgentRun BuildFromPlan missing workspace revision returns controlled failed run")]
    public async Task CreateRun_BuildFromPlanMissingWorkspaceRevision_ShouldNotStartBackgroundRun()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var ownerHash = ResolveOwnerHashForTest("user-default");
        host.ConversationService.GetOrCreateOwnedSession(ownerHash, "session-build-missing-revision")
            .Status.Should().Be(ConversationOwnedSessionStatus.Ready);
        var initial = host.ConversationService.TryUpdateOwnedWorkspaceSnapshot(
            ownerHash,
            "session-build-missing-revision",
            new VisionAgentWorkspaceSnapshotUpdate
            {
                LifecycleState = "plan_ready",
                RequirementMode = AiRequirementModes.Strict
            });

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new AgentRunCreateRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Target = NewProjectTarget(),
            Description = "Build from persisted plan without revision",
            SessionId = "session-build-missing-revision",
            Mode = "new",
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = BuildableAgentRunBuildFromPlanRequest()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("errorCode").GetString().Should().Be("workspace_revision_required");
        root.GetProperty("workspaceSnapshot").GetProperty("revision").GetInt64()
            .Should().Be(initial.Snapshot!.Revision);
        JsonSerializer.Serialize(root.GetProperty("events")).Should().Contain("workspace_revision_required");
        host.Generation.LastCommand.Should().BeNull();
        host.ConversationService.GetSession("session-build-missing-revision")!
            .WorkspaceSnapshot!
            .BuildRunId
            .Should().BeNull();
    }

    [Fact(DisplayName = "POST AgentRun BuildFromPlan missing revision is blocked when workspace is created concurrently")]
    public async Task CreateRun_BuildFromPlanConcurrentWorkspaceCreationWithoutRevision_ShouldNotStartBackgroundRun()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var sessionId = "session-build-concurrent-missing-revision";
        var ownerHash = ResolveOwnerHashForTest("user-default");
        host.ConversationService.GetOrCreateOwnedSession(ownerHash, sessionId)
            .Status.Should().Be(ConversationOwnedSessionStatus.Ready);
        var updateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstWrite = 1;
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = () =>
        {
            if (Interlocked.Exchange(ref firstWrite, 0) != 1)
            {
                return;
            }

            updateEntered.SetResult();
            releaseUpdate.Task.GetAwaiter().GetResult();
        };

        var workspaceTask = Task.Run(() => host.ConversationService.TryUpdateOwnedWorkspaceSnapshot(
            ownerHash,
            sessionId,
            new VisionAgentWorkspaceSnapshotUpdate
            {
                LifecycleState = "plan_ready",
                RequirementMode = AiRequirementModes.Strict
            }));
        await updateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var buildTask = host.Client.PostAsJsonAsync("/api/ai/agent-runs", new AgentRunCreateRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Target = NewProjectTarget(),
            Description = "Build from concurrently persisted plan without revision",
            SessionId = sessionId,
            Mode = "new",
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = BuildableAgentRunBuildFromPlanRequest()
        });
        releaseUpdate.SetResult();
        var initial = await workspaceTask.WaitAsync(TimeSpan.FromSeconds(5));
        using var response = await buildTask.WaitAsync(TimeSpan.FromSeconds(5));

        initial.Success.Should().BeTrue();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("errorCode").GetString().Should().Be("workspace_revision_required");
        root.GetProperty("workspaceSnapshot").GetProperty("revision").GetInt64()
            .Should().Be(initial.Revision);
        host.Generation.LastCommand.Should().BeNull();
        host.ConversationService.GetSession(sessionId)!
            .WorkspaceSnapshot!
            .BuildRunId
            .Should().BeNull();
    }

    [Fact(DisplayName = "POST AgentRun BuildFromPlan backup persistence failure still starts background run")]
    public async Task CreateRun_BuildFromPlanBackupPersistenceFailure_ShouldStartBackgroundRunWithDegradedStatus()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        host.ConcreteConversationService.RecoveryBackupWriteFaultInjector = () => throw new IOException("backup failed");

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new AgentRunCreateRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Target = NewProjectTarget(),
            Description = "Build from persisted plan",
            SessionId = "session-build-backup-fail",
            Mode = "new",
            UseVisionAgentGenerateFlow = true,
            BuildFromPlan = BuildableAgentRunBuildFromPlanRequest()
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("persistenceStatus").GetProperty("primaryStoreSaved").GetBoolean().Should().BeTrue();
        root.GetProperty("persistenceStatus").GetProperty("recoveryBackupSaved").GetBoolean().Should().BeFalse();
        var runId = root.GetProperty("runId").GetString()!;
        root.GetProperty("workspaceSnapshot").GetProperty("buildRunId").GetString().Should().Be(runId);
        await host.Generation.WaitForCallAsync();
        host.Generation.LastCommand.Should().NotBeNull();
        host.Generation.LastCommand!.RunId.Should().Be(runId);
    }

    [Fact(DisplayName = "POST AgentRun BuildFromPlan failure replays canonical BuildReadiness payload")]
    public async Task CreateRun_BuildFromPlanFailure_ShouldReplayBuildReadiness()
    {
        var readiness = new VisionAgentBuildReadinessSnapshot
        {
            CanBuild = false,
            RemainingFields = ["image_source", "acceptance_criteria"],
            ResolvedFields = ["inspection_object", "task_type"],
            Blockers =
            [
                new VisionAgentBuildBlocker
                {
                    Id = "hard_requirement:image_source",
                    Category = VisionAgentBuildBlockerCategories.HardRequirement,
                    Field = "image_source",
                    BlocksBuild = true,
                    ResolutionMode = VisionAgentBuildBlockerResolutionModes.AnswerQuestion
                }
            ],
            PrimaryMessage = "Canonical readiness blocked Build.",
            ContractVersion = VisionAgentPlanContractVersions.V2
        };
        await using var host = await AgentRunEndpointTestHost.CreateAsync((_, _) => Task.FromResult(new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusClarificationRequired,
            FailureType = AiFlowGenerationResult.FailureTypeClarificationRequired,
            ClarificationRequired = true,
            ErrorMessage = "Canonical readiness blocked Build.",
            FailureSummary = new AiFailureSummary
            {
                Category = "requirement_maturity",
                Code = "maturity_gate_blocked",
                Message = "Canonical readiness blocked Build.",
                RepairTarget = "Answer canonical fields."
            },
            BuildReadiness = readiness,
            BlockingClarificationFields = ["image_source", "acceptance_criteria"],
            RequirementMaturity = new AiRequirementMaturityResult
            {
                CanPlan = true,
                CanBuild = false,
                MissingFields = ["image_source", "acceptance_criteria"],
                PublicReason = "Canonical readiness blocked Build."
            }
        }));

        var plan = LegacyBlockedAgentRunBuildFromPlanSnapshot();
        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new AgentRunCreateRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Target = NewProjectTarget(),
            Description = "start build from blocked canonical plan",
            BuildFromPlan = new VisionAgentBuildFromPlanRequest
            {
                PlanId = plan.PlanId,
                PlanHash = plan.PlanHash,
                PlanSnapshot = plan,
                ConfirmedAnswers = ConfirmedAgentRunBuildFromPlanAnswers(),
                MetadataOnly = true
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var createDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = createDoc.RootElement.GetProperty("runId").GetString()!;
        await host.Generation.WaitForCallAsync();
        await host.WaitForTerminalAsync(runId);

        var replay = host.StreamService.Replay(runId)!;
        replay.Summary.Status.Should().Be(AgentRunEventStatuses.Failed);
        var failed = replay.Events.Single(evt => evt.EventType == AgentRunEventTypes.RunFailed);
        var failedJson = JsonSerializer.Serialize(failed, AgentRunEventJson.Options);
        failedJson.Should().Contain("\"buildReadiness\"");
        failedJson.Should().Contain("\"canBuild\":false");
        failedJson.Should().Contain("\"remainingFields\":[\"image_source\",\"acceptance_criteria\"]");
        failedJson.Should().Contain("\"buildFromPlan\"");
        failedJson.Should().Contain("\"planSnapshot\"");
        failedJson.Should().Contain($"\"planId\":\"{plan.PlanId}\"");
        failedJson.Should().Contain($"\"planHash\":\"{plan.PlanHash}\"");
    }

    [Fact(DisplayName = "GET AgentRun replay returns final summary and events")]
    public async Task Replay_ShouldReturnSummaryAndEvents()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var runId = await host.CreateRunAsync("Generate wire sequence inspection");

        await host.Generation.WaitForCallAsync();
        await host.WaitForTerminalAsync(runId);

        using var response = await host.Client.GetAsync($"/api/ai/agent-runs/{runId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("summary").GetProperty("status").GetString().Should().Be(AgentRunEventStatuses.Completed);
        document.RootElement.GetProperty("events").EnumerateArray()
            .Select(evt => evt.GetProperty("eventType").GetString())
            .Should()
            .Contain(AgentRunEventTypes.RunCompleted);
        var eventCount = document.RootElement.GetProperty("events").EnumerateArray().Count();
        var snapshot = document.RootElement.GetProperty("snapshot");
        snapshot.GetProperty("storageVersion").GetString().Should().Be(AgentRunEventStore.StorageVersion);
        snapshot.GetProperty("runId").GetString().Should().Be(runId);
        snapshot.GetProperty("firstSequence").GetInt64().Should().Be(1);
        snapshot.GetProperty("lastSequence").GetInt64().Should().BeGreaterThanOrEqualTo(3);
        snapshot.GetProperty("eventCount").GetInt32().Should().Be(eventCount);
        snapshot.GetProperty("events").EnumerateArray()
            .Select(evt => evt.GetProperty("sequence").GetInt64())
            .Should()
            .BeInAscendingOrder();
        var diagnostics = document.RootElement.GetProperty("diagnostics");
        diagnostics.GetProperty("runId").GetString().Should().Be(runId);
        diagnostics.GetProperty("eventCount").GetInt32().Should().Be(eventCount);
        diagnostics.GetProperty("duplicateEventCount").GetInt32().Should().Be(0);
        diagnostics.GetProperty("droppedEventCount").GetInt32().Should().Be(0);
        diagnostics.GetProperty("staleEventCount").GetInt32().Should().Be(0);

        var completed = document.RootElement.GetProperty("events").EnumerateArray()
            .Single(evt => evt.GetProperty("eventType").GetString() == AgentRunEventTypes.RunCompleted);
        completed.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
        completed.GetProperty("payload").GetProperty("flow").ValueKind.Should().Be(JsonValueKind.Object);
        completed.GetProperty("payload").GetProperty("toolTraceCount").GetInt32().Should().Be(1);
        completed.GetProperty("payload").GetProperty("pendingParameterCount").GetInt32().Should().Be(0);
        completed.GetProperty("payload").GetProperty("missingResourceCount").GetInt32().Should().Be(0);

        var payloadJson = completed.GetProperty("payload").GetRawText();
        payloadJson.Should().NotContain("promptTrace");
        payloadJson.Should().NotContain("reasoningContent");
        payloadJson.Should().NotContain("systemPrompt");
        payloadJson.Should().NotContain("rawPrompt");
    }

    [Fact(DisplayName = "GET latest AgentRun replay returns newest owner run")]
    public async Task ReplayLatest_ShouldReturnNewestOwnerRun()
    {
        var now = DateTimeOffset.Parse("2026-06-07T00:00:00Z");
        await using var host = await AgentRunEndpointTestHost.CreateAsync(useAuth: true, utcNowProvider: () => now);
        var ownerA = ResolveOwnerHashForTest("user-owner-a");
        var ownerB = ResolveOwnerHashForTest("user-owner-b");
        var oldA = host.StreamService.CreateRun("owner A old", ownerHash: ownerA);
        host.StreamService.Complete(oldA.RunId, "old done");

        now = now.AddMinutes(1);
        var latestB = host.StreamService.CreateRun("owner B latest", ownerHash: ownerB);
        host.StreamService.Complete(latestB.RunId, "owner B done");

        now = now.AddMinutes(1);
        var latestA = host.StreamService.CreateRun("owner A latest", ownerHash: ownerA);
        host.StreamService.Append(latestA.RunId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.StageStarted,
            Stage = "planner",
            Title = "Planner started",
            Summary = "Planner replay is available.",
            Status = AgentRunEventStatuses.Running
        });

        host.AuthorizeAs("owner-a-token");
        using var ownerAResponse = await host.Client.GetAsync("/api/ai/agent-runs/latest");
        ownerAResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var ownerADoc = JsonDocument.Parse(await ownerAResponse.Content.ReadAsStringAsync());
        ownerADoc.RootElement.GetProperty("summary").GetProperty("runId").GetString().Should().Be(latestA.RunId);
        ownerADoc.RootElement.GetProperty("snapshot").GetProperty("events").EnumerateArray()
            .Select(evt => evt.GetProperty("eventType").GetString())
            .Should()
            .Contain(AgentRunEventTypes.StageStarted);

        host.AuthorizeAs("owner-b-token");
        using var ownerBResponse = await host.Client.GetAsync("/api/ai/agent-runs/latest");
        ownerBResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var ownerBDoc = JsonDocument.Parse(await ownerBResponse.Content.ReadAsStringAsync());
        ownerBDoc.RootElement.GetProperty("summary").GetProperty("runId").GetString().Should().Be(latestB.RunId);
    }

    [Fact(DisplayName = "GET AgentRun missing replay returns 404")]
    public async Task ReplayMissingRun_ShouldReturnNotFound()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();

        using var response = await host.Client.GetAsync("/api/ai/agent-runs/ar_missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "GET AgentRun SSE streams replay frames")]
    public async Task Events_ShouldStreamReplayFrames()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var runId = await host.CreateRunAsync("SSE replay");

        await host.Generation.WaitForCallAsync();
        await host.WaitForTerminalAsync(runId);

        using var response = await host.Client.GetAsync($"/api/ai/agent-runs/{runId}/events", HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("event: run.started");
        body.Should().Contain("event: assistant.brief");
        body.Should().Contain("event: run.completed");
        body.Should().Contain("data: ");
    }

    [Fact(DisplayName = "GET AgentRun SSE honors Last-Event-ID replay cursor")]
    public async Task Events_ShouldHonorLastEventId()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var runId = await host.CreateRunAsync("SSE cursor");

        await host.Generation.WaitForCallAsync();
        await host.WaitForTerminalAsync(runId);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ai/agent-runs/{runId}/events");
        request.Headers.TryAddWithoutValidation("Last-Event-ID", "2");
        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("event: run.started");
        body.Should().NotContain("event: assistant.brief");
        body.Should().Contain("event: run.completed");
    }

    [Fact(DisplayName = "GET AgentRun SSE streams live event before terminal")]
    public async Task Events_ShouldStreamLiveFrameBeforeTerminal()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(async (_, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return AgentRunEndpointTestHost.SuccessResult();
        });
        var runId = await host.CreateRunAsync("live endpoint stream");
        await host.Generation.WaitForCallAsync();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ai/agent-runs/{runId}/events");
            request.Headers.TryAddWithoutValidation("Last-Event-ID", "1");
            using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            host.StreamService.Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.ToolCallCompleted,
                Stage = "planner",
                Title = "Tool completed: live_probe",
                Summary = "Live frame before terminal.",
                Status = AgentRunEventStatuses.Completed,
                Payload = new { metadataOnly = true }
            });

            var frame = await ReadSseUntilAsync(response, AgentRunEventTypes.ToolCallCompleted);
            frame.Should().Contain("event: tool.call.completed");
            frame.Should().Contain("Live frame before terminal.");
            host.StreamService.Replay(runId)!.Events.Should().NotContain(evt => evt.EventType == AgentRunEventTypes.RunCompleted);
        }
        finally
        {
            gate.TrySetResult();
        }
        await host.WaitForTerminalAsync(runId);
    }

    [Fact(DisplayName = "GET AgentRun SSE receives cancel terminal and closes")]
    public async Task Events_ShouldReceiveCancelTerminalAndClose()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(async (_, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return AgentRunEndpointTestHost.SuccessResult();
        });
        var runId = await host.CreateRunAsync("cancel stream");
        await host.Generation.WaitForCallAsync();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ai/agent-runs/{runId}/events");
            request.Headers.TryAddWithoutValidation("Last-Event-ID", "1");
            using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            using var cancel = await host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);
            cancel.StatusCode.Should().Be(HttpStatusCode.OK);

            var frame = await ReadSseUntilAsync(response, AgentRunEventTypes.RunCancelled);
            frame.Should().Contain("event: run.cancelled");
        }
        finally
        {
            gate.TrySetResult();
        }
    }

    [Fact(DisplayName = "GET AgentRun replay cursor fills missed history after reconnect")]
    public async Task Events_ShouldReplayMissedHistoryAfterReconnect()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(async (_, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return AgentRunEndpointTestHost.SuccessResult();
        });
        var runId = await host.CreateRunAsync("reconnect replay");
        await host.Generation.WaitForCallAsync();
        host.StreamService.Append(runId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.ToolCallCompleted,
            Stage = "readiness",
            Title = "Tool completed: validate_flow",
            Summary = "Replay should include this missed event.",
            Status = AgentRunEventStatuses.Completed,
            Payload = new { toolName = "validate_flow", metadataOnly = true }
        });
        gate.SetResult();
        await host.WaitForTerminalAsync(runId);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ai/agent-runs/{runId}/events");
        request.Headers.TryAddWithoutValidation("Last-Event-ID", "2");
        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("event: tool.call.completed");
        body.Should().Contain("event: run.completed");
        body.IndexOf("event: tool.call.completed", StringComparison.Ordinal)
            .Should()
            .BeLessThan(body.IndexOf("event: run.completed", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "AgentRun events require Authorization for fetch stream")]
    public async Task AuthenticatedEvents_ShouldRequireAuthorizationAndAllowBearerFetch()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(useAuth: true);
        host.AuthorizeAs("owner-a-token");
        var runId = await host.CreateRunAsync("auth stream");
        await host.WaitForTerminalAsync(runId);

        using var unauthorizedClient = host.CreateAnonymousClient();
        using var unauthorized = await unauthorizedClient.GetAsync($"/api/ai/agent-runs/{runId}/events", HttpCompletionOption.ResponseHeadersRead);
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var authorized = await host.Client.GetAsync($"/api/ai/agent-runs/{runId}/events", HttpCompletionOption.ResponseHeadersRead);
        authorized.StatusCode.Should().Be(HttpStatusCode.OK);
        authorized.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    }

    [Fact(DisplayName = "AgentRun ownership is enforced across replay cancel events and stream token")]
    public async Task AuthenticatedRuns_ShouldRejectWrongOwner()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(async (_, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return AgentRunEndpointTestHost.SuccessResult();
        }, useAuth: true);
        host.AuthorizeAs("owner-a-token");
        var runId = await host.CreateRunAsync("owner protected");
        await host.Generation.WaitForCallAsync();

        try
        {
            host.AuthorizeAs("owner-b-token");
            using var replay = await host.Client.GetAsync($"/api/ai/agent-runs/{runId}");
            using var cancel = await host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);
            using var token = await host.Client.PostAsync($"/api/ai/agent-runs/{runId}/stream-token", content: null);

            replay.StatusCode.Should().Be(HttpStatusCode.NotFound);
            cancel.StatusCode.Should().Be(HttpStatusCode.NotFound);
            token.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            host.AuthorizeAs("owner-a-token");
            gate.TrySetResult();
        }
        await host.WaitForTerminalAsync(runId);
    }

    [Fact(DisplayName = "AgentRun EventSource stream token is single-use run-bound and expires")]
    public async Task EventSourceStreamToken_ShouldBeSingleUseRunBoundAndExpire()
    {
        var now = DateTimeOffset.UtcNow;
        await using var host = await AgentRunEndpointTestHost.CreateAsync(useAuth: true, utcNowProvider: () => now);
        host.AuthorizeAs("owner-a-token");
        var runId = await host.CreateRunAsync("stream token");
        await host.WaitForTerminalAsync(runId);
        var streamToken = await host.CreateStreamTokenAsync(runId);

        using var wrongRun = await host.Client.GetAsync($"/api/ai/agent-runs/ar_wrong/events?streamToken={Uri.EscapeDataString(streamToken)}", HttpCompletionOption.ResponseHeadersRead);
        wrongRun.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var authorized = await host.CreateAnonymousClient().GetAsync($"/api/ai/agent-runs/{runId}/events?streamToken={Uri.EscapeDataString(streamToken)}", HttpCompletionOption.ResponseHeadersRead);
        authorized.StatusCode.Should().Be(HttpStatusCode.OK);

        using var reused = await host.CreateAnonymousClient().GetAsync($"/api/ai/agent-runs/{runId}/events?streamToken={Uri.EscapeDataString(streamToken)}", HttpCompletionOption.ResponseHeadersRead);
        reused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var expiringToken = await host.CreateStreamTokenAsync(runId);
        now = now.AddSeconds(61);
        using var expired = await host.CreateAnonymousClient().GetAsync($"/api/ai/agent-runs/{runId}/events?streamToken={Uri.EscapeDataString(expiringToken)}", HttpCompletionOption.ResponseHeadersRead);
        expired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "AI Session HTTP isolates owners and hides session operation existence")]
    public async Task AiSessionEndpoints_ShouldIsolateOwnersAndHideExistence()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(useAuth: true);
        var clientOperationId = Guid.NewGuid();
        host.AuthorizeAs("owner-a-token");
        using var create = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createDocument = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var sessionId = createDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString()!;

        using var ownerList = await host.Client.GetAsync("/api/ai/sessions");
        ownerList.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ownerList.Content.ReadAsStringAsync()).Should().Contain(sessionId);

        host.AuthorizeAs("owner-b-token");
        using var otherList = await host.Client.GetAsync("/api/ai/sessions");
        using var otherListDocument = JsonDocument.Parse(await otherList.Content.ReadAsStringAsync());
        otherListDocument.RootElement.GetProperty("total").GetInt32().Should().Be(0);

        using var get = await host.Client.GetAsync($"/api/ai/sessions/{sessionId}");
        using var update = await host.Client.PostAsJsonAsync($"/api/ai/sessions/{sessionId}/workspace-snapshot", new
        {
            expectedRevision = 0,
            clientMutationId = Guid.NewGuid().ToString("D"),
            lifecycleState = "plan_ready"
        });
        using var delete = await host.Client.DeleteAsync(
            $"/api/ai/sessions/{sessionId}?expectedRevision=0&clientMutationId={Guid.NewGuid():D}");
        using var operation = await host.Client.GetAsync(
            $"/api/ai/operations/{clientOperationId:D}?kind={AiOperationKinds.SessionCreate}");

        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        update.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        operation.StatusCode.Should().Be(HttpStatusCode.NotFound);

        host.AuthorizeAs("owner-a-token");
        using var ownerOperation = await host.Client.GetAsync(
            $"/api/ai/operations/{clientOperationId:D}?kind={AiOperationKinds.SessionCreate}");
        ownerOperation.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "AI Run history is owner scoped, paged, session filterable and redacted")]
    public async Task AiRunHistory_ShouldBeOwnerScopedPagedAndRedacted()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(useAuth: true);
        host.AuthorizeAs("owner-a-token");
        using var create = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId = Guid.NewGuid() });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createDocument = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var sessionId = createDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString()!;
        var planRunId = await host.CreatePlanRunAsync("plan owner scoped history", sessionId);
        var buildRunId = await host.CreateRunAsync("build owner scoped history");
        await host.WaitForTerminalAsync(planRunId);
        await host.WaitForTerminalAsync(buildRunId);

        using var firstPage = await host.Client.GetAsync("/api/ai/agent-runs?offset=0&limit=1");
        firstPage.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstJson = await firstPage.Content.ReadAsStringAsync();
        using var firstDocument = JsonDocument.Parse(firstJson);
        firstDocument.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        firstDocument.RootElement.GetProperty("items").GetArrayLength().Should().Be(1);
        var item = firstDocument.RootElement.GetProperty("items")[0];
        item.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
        [
            "runId", "sessionId", "kind", "status", "title", "summary", "firstFixRecommendation",
            "recoveryState", "createdAtUtc", "updatedAtUtc", "lastSequence", "eventCount"
        ]);
        firstJson.ToLowerInvariant().Should().NotContain("ownerhash");
        firstJson.ToLowerInvariant().Should().NotContain("terminalintent");
        firstJson.ToLowerInvariant().Should().NotContain("payload");
        firstJson.ToLowerInvariant().Should().NotContain("reasoning");

        using var secondPage = await host.Client.GetAsync("/api/ai/agent-runs?offset=1&limit=1");
        using var secondDocument = JsonDocument.Parse(await secondPage.Content.ReadAsStringAsync());
        secondDocument.RootElement.GetProperty("offset").GetInt32().Should().Be(1);
        secondDocument.RootElement.GetProperty("items").GetArrayLength().Should().Be(1);

        using var sessionPage = await host.Client.GetAsync(
            $"/api/ai/agent-runs?offset=0&limit=10&sessionId={Uri.EscapeDataString(sessionId)}");
        using var sessionDocument = JsonDocument.Parse(await sessionPage.Content.ReadAsStringAsync());
        sessionDocument.RootElement.GetProperty("total").GetInt32().Should().Be(1);
        sessionDocument.RootElement.GetProperty("items")[0].GetProperty("runId").GetString().Should().Be(planRunId);
        sessionDocument.RootElement.GetProperty("items")[0].GetProperty("kind").GetString().Should().Be("plan");

        host.AuthorizeAs("owner-b-token");
        using var otherOwner = await host.Client.GetAsync("/api/ai/agent-runs?offset=0&limit=10");
        using var otherOwnerDocument = JsonDocument.Parse(await otherOwner.Content.ReadAsStringAsync());
        otherOwnerDocument.RootElement.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact(DisplayName = "AI Session delete blocks pending operations, active artifacts and staged drafts")]
    public async Task AiSessionDelete_ShouldFailClosedForUnsafeAssociations()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(useAuth: true);
        host.AuthorizeAs("owner-a-token");

        async Task<(string SessionId, string OwnerHash)> CreateSessionAsync()
        {
            using var response = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId = Guid.NewGuid() });
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var id = document.RootElement.GetProperty("session").GetProperty("sessionId").GetString()!;
            return (id, host.ConversationService.GetSession(id)!.OwnerHash!);
        }

        async Task<JsonElement> DeleteAsync(string sessionId, Guid mutationId)
        {
            using var response = await host.Client.DeleteAsync(
                $"/api/ai/sessions/{sessionId}?expectedRevision=0&clientMutationId={mutationId:D}");
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        var pending = await CreateSessionAsync();
        var pendingOperationId = Guid.NewGuid();
        host.OperationStore.Reserve(
            pending.OwnerHash,
            AiOperationKinds.PlanRun,
            pendingOperationId,
            "sha256:" + new string('a', 64),
            pending.SessionId);
        var pendingDeleteId = Guid.NewGuid();
        var pendingConflict = await DeleteAsync(pending.SessionId, pendingDeleteId);
        pendingConflict.GetProperty("errorCode").GetString().Should().Be("session_active_operation_conflict");
        using var pendingReceipt = await host.Client.GetAsync(
            $"/api/ai/operations/{pendingDeleteId:D}?kind={AiOperationKinds.SessionDelete}");
        using var pendingReceiptDocument = JsonDocument.Parse(await pendingReceipt.Content.ReadAsStringAsync());
        pendingReceiptDocument.RootElement.GetProperty("status").GetString().Should().Be(AiOperationStatuses.Rejected);

        var available = await CreateSessionAsync();
        var availableArtifact = host.HandoffStore.Create(HandoffCommand(available.OwnerHash, available.SessionId));
        availableArtifact.Outcome.Should().Be(AiWorkspaceHandoffStoreOutcome.Created);
        var availableConflict = await DeleteAsync(available.SessionId, Guid.NewGuid());
        availableConflict.GetProperty("errorCode").GetString().Should().Be("session_active_artifact_conflict");

        var staged = await CreateSessionAsync();
        var stagedArtifact = host.HandoffStore.Create(HandoffCommand(staged.OwnerHash, staged.SessionId)).Artifact!;
        var consumeOperationId = Guid.NewGuid();
        host.HandoffStore.ReserveConsume(staged.OwnerHash, stagedArtifact.ArtifactId, consumeOperationId, null)
            .Outcome.Should().Be(AiWorkspaceHandoffStoreOutcome.Updated);
        host.HandoffStore.Acknowledge(staged.OwnerHash, stagedArtifact.ArtifactId, consumeOperationId, null)
            .Outcome.Should().Be(AiWorkspaceHandoffStoreOutcome.Updated);
        var stagedConflict = await DeleteAsync(staged.SessionId, Guid.NewGuid());
        stagedConflict.GetProperty("errorCode").GetString().Should().Be("session_staged_draft_conflict");

        static AiWorkspaceHandoffCreateCommand HandoffCommand(string ownerHash, string sessionId) => new()
        {
            OwnerHash = ownerHash,
            ClientOperationId = Guid.NewGuid(),
            SessionId = sessionId,
            SessionRevision = 0,
            PlanRunId = "run_plan_history",
            PlanId = "plan_history",
            PlanHash = new string('b', 64),
            BuildRunId = "run_build_history",
            BuildClientOperationId = Guid.NewGuid(),
            BuildIdentity = "build:history",
            SubmittedBuildFingerprint = new string('c', 64),
            TargetKind = "new",
            CandidateFlowJson = "{}",
            CandidateFlowFingerprint = new string('d', 64),
            PublicBuild = new VisionAgentPublicBuildResultV1()
        };
    }

    [Fact(DisplayName = "AI Session delete independently blocks active Plan and Build runs")]
    public async Task AiSessionDelete_ShouldFailClosedForActivePlanAndBuildRuns()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(useAuth: true);
        host.AuthorizeAs("owner-a-token");

        foreach (var runKind in new[] { "plan", "build" })
        {
            using var create = await host.Client.PostAsJsonAsync(
                "/api/ai/sessions",
                new { clientOperationId = Guid.NewGuid() });
            create.EnsureSuccessStatusCode();
            using var createDocument = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
            var createdSession = createDocument.RootElement.GetProperty("session");
            var sessionId = createdSession.GetProperty("sessionId").GetString()!;
            var ownerHash = host.ConversationService.GetSession(sessionId)!.OwnerHash!;
            var activeRun = host.StreamService.CreateRun(
                $"active {runKind} delete guard",
                ownerHash: ownerHash);
            var update = new VisionAgentWorkspaceSnapshotUpdate
            {
                ExpectedRevision = 0,
                ClientMutationId = Guid.NewGuid().ToString("D"),
                LifecycleState = runKind == "plan" ? "planning" : "building",
                PlanRunId = runKind == "plan" ? activeRun.RunId : null,
                PlanRunStatus = runKind == "plan" ? AgentRunEventStatuses.Running : null,
                BuildRunId = runKind == "build" ? activeRun.RunId : null,
                BuildRunStatus = runKind == "build" ? AgentRunEventStatuses.Running : null
            };
            var seeded = host.ConversationService.TryUpdateOwnedWorkspaceSnapshot(ownerHash, sessionId, update);
            seeded.Success.Should().BeTrue(runKind);

            var deleteMutationId = Guid.NewGuid();
            using var delete = await host.Client.DeleteAsync(
                $"/api/ai/sessions/{sessionId}?expectedRevision={seeded.Revision}&clientMutationId={deleteMutationId:D}");
            delete.StatusCode.Should().Be(HttpStatusCode.Conflict, runKind);
            using var deleteDocument = JsonDocument.Parse(await delete.Content.ReadAsStringAsync());
            deleteDocument.RootElement.GetProperty("errorCode").GetString()
                .Should().Be("session_active_run_conflict", runKind);
            deleteDocument.RootElement.GetProperty("operation").GetProperty("status").GetString()
                .Should().Be(AiOperationStatuses.Rejected, runKind);
            host.ConversationService.GetSession(sessionId).Should().NotBeNull(runKind);

            host.StreamService.Cancel(activeRun.RunId);
        }
    }

    [Fact(DisplayName = "AI Session delete is idempotently reconciled and never deletes a Project")]
    public async Task AiSessionDelete_ShouldReconcileByMutationReceiptWithoutProjectCascade()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var projectId = Guid.NewGuid();
        host.Projects.Project = new ProjectDto
        {
            Id = projectId,
            Name = "delete-isolation-project",
            PersistenceRevision = 1,
            Flow = new OperatorFlowDto { Id = Guid.NewGuid(), Name = "delete-isolation-flow" }
        };
        using var create = await host.Client.PostAsJsonAsync("/api/ai/sessions", new
        {
            clientOperationId = Guid.NewGuid(),
            projectId
        });
        create.EnsureSuccessStatusCode();
        using var createDocument = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var createdSession = createDocument.RootElement.GetProperty("session");
        var sessionId = createdSession.GetProperty("sessionId").GetString()!;
        var expectedRevision = createdSession.GetProperty("snapshot").GetProperty("revision").GetInt64();
        var mutationId = Guid.NewGuid();
        var uri = $"/api/ai/sessions/{sessionId}?expectedRevision={expectedRevision}&clientMutationId={mutationId:D}";

        using var first = await host.Client.DeleteAsync(uri);
        using var replay = await host.Client.DeleteAsync(uri);
        using var missing = await host.Client.GetAsync($"/api/ai/sessions/{sessionId}");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).Should().Contain("\"deleted\":true");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Projects.Project.Should().NotBeNull();
        host.Projects.Project!.Id.Should().Be(projectId);
    }

    [Fact(DisplayName = "AI endpoints allow Admin and Engineer but reject Operator and unauthenticated users")]
    public async Task AiEndpoints_ShouldEnforceRoleAndAuthenticationMatrix()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(useAuth: true);
        host.Projects.Project = new ProjectDto
        {
            Id = Guid.NewGuid(),
            Name = "auth-matrix-project",
            PersistenceRevision = 1,
            Flow = new OperatorFlowDto { Id = Guid.NewGuid(), Name = "auth-flow" }
        };

        host.AuthorizeAs("owner-a-token");
        using var admin = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId = Guid.NewGuid() });
        using var adminBaseline = await host.Client.GetAsync($"/api/ai/projects/{host.Projects.Project.Id:D}/baseline");
        using var adminRevalidate = await host.Client.PostAsJsonAsync("/api/ai/agent-runs/ar_missing/revalidate", new
        {
            sessionId = "missing",
            clientMutationId = Guid.NewGuid(),
            buildId = "build_missing",
            candidateFlowFingerprint = new string('a', 64)
        });
        admin.StatusCode.Should().Be(HttpStatusCode.Created);
        adminBaseline.StatusCode.Should().Be(HttpStatusCode.OK);
        adminRevalidate.StatusCode.Should().Be(HttpStatusCode.NotFound);

        host.AuthorizeAs("engineer-token");
        using var engineer = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId = Guid.NewGuid() });
        using var engineerBaseline = await host.Client.GetAsync($"/api/ai/projects/{host.Projects.Project.Id:D}/baseline");
        using var engineerRevalidate = await host.Client.PostAsJsonAsync("/api/ai/agent-runs/ar_missing/revalidate", new
        {
            sessionId = "missing",
            clientMutationId = Guid.NewGuid(),
            buildId = "build_missing",
            candidateFlowFingerprint = new string('a', 64)
        });
        engineer.StatusCode.Should().Be(HttpStatusCode.Created);
        engineerBaseline.StatusCode.Should().Be(HttpStatusCode.OK);
        engineerRevalidate.StatusCode.Should().Be(HttpStatusCode.NotFound);

        host.AuthorizeAs("operator-token");
        using var operatorSession = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId = Guid.NewGuid() });
        using var operatorBuild = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            clientOperationId = Guid.NewGuid(),
            target = new { targetKind = "new" },
            description = "operator must not build"
        });
        using var operatorRunHistory = await host.Client.GetAsync("/api/ai/agent-runs?offset=0&limit=10");
        using var operatorBaseline = await host.Client.GetAsync($"/api/ai/projects/{host.Projects.Project.Id:D}/baseline");
        using var operatorRevalidate = await host.Client.PostAsJsonAsync("/api/ai/agent-runs/ar_missing/revalidate", new
        {
            sessionId = "missing",
            clientMutationId = Guid.NewGuid()
        });
        operatorSession.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        operatorBuild.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        operatorRunHistory.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        operatorBaseline.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        operatorRevalidate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var anonymous = host.CreateAnonymousClient();
        using var unauthenticated = await anonymous.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId = Guid.NewGuid() });
        using var anonymousRunHistory = await anonymous.GetAsync("/api/ai/agent-runs?offset=0&limit=10");
        using var anonymousBaseline = await anonymous.GetAsync($"/api/ai/projects/{host.Projects.Project.Id:D}/baseline");
        using var anonymousRevalidate = await anonymous.PostAsJsonAsync("/api/ai/agent-runs/ar_missing/revalidate", new
        {
            sessionId = "missing",
            clientMutationId = Guid.NewGuid()
        });
        unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymousRunHistory.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymousBaseline.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymousRevalidate.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "AI Session revision conflict returns only the latest public snapshot")]
    public async Task AiSessionMutationConflict_ShouldReturnStrictPublicSnapshot()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        using var create = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId = Guid.NewGuid() });
        using var createDocument = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var sessionId = createDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString()!;

        using var update = await host.Client.PostAsJsonAsync($"/api/ai/sessions/{sessionId}/workspace-snapshot", new
        {
            expectedRevision = 0,
            clientMutationId = Guid.NewGuid().ToString("D"),
            lifecycleState = "plan_ready",
            planQuestionSelections = new Dictionary<string, string>
            {
                ["authorization"] = "Bearer secret-token"
            }
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        using var conflict = await host.Client.PostAsJsonAsync($"/api/ai/sessions/{sessionId}/workspace-snapshot", new
        {
            expectedRevision = 0,
            clientMutationId = Guid.NewGuid().ToString("D"),
            lifecycleState = "building"
        });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflictJson = await conflict.Content.ReadAsStringAsync();
        using var conflictDocument = JsonDocument.Parse(conflictJson);
        var root = conflictDocument.RootElement;
        root.GetProperty("errorCode").GetString().Should().Be("workspace_revision_conflict");
        var latest = root.GetProperty("latestSnapshot");
        latest.GetProperty("revision").GetInt64().Should().Be(1);
        var allowedSnapshotKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "schemaVersion", "revision", "projectId", "lifecycleState", "planRunId", "planRunStatus",
            "buildRunId", "buildRunStatus", "buildTerminalSequence", "buildClientOperationId",
            "submittedBuildFingerprint", "projectBaseline", "requirementMode",
            "planQuestionSelections", "confirmedPlanAnswers", "optimisticPlanAnswers", "answerRevision",
            "buildParameterValues", "readinessPreview", "missingResources", "resourceDecisions",
            "resourceRevision", "buildResult", "planAcceptedRecommendedDefaults", "planTerminalSequence", "updatedAtUtc"
        };
        foreach (var property in latest.EnumerateObject())
        {
            allowedSnapshotKeys.Should().Contain(property.Name);
        }
        var normalizedConflictJson = conflictJson.ToLowerInvariant();
        normalizedConflictJson.Should().NotContain("authorization");
        normalizedConflictJson.Should().NotContain("secret-token");
        normalizedConflictJson.Should().NotContain("reasoning");
        normalizedConflictJson.Should().NotContain("rawpayload");
        normalizedConflictJson.Should().NotContain("pendingplansnapshot");
        normalizedConflictJson.Should().NotContain("mutationreceipts");
    }

    [Fact(DisplayName = "AI Session resolves camera identities through backend authority and advances canonical resource revision")]
    public async Task AiSessionResourceDecision_ShouldUseBackendAuthorityAndCanonicalRevision()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        using var create = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId = Guid.NewGuid() });
        using var createDocument = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var sessionId = createDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString()!;

        static VisionAgentResourceRequirement Missing(
            string resourceType,
            string operatorKey,
            string operatorId,
            string parameterName) => new()
        {
            CanonicalId = VisionAgentResourceIdentity.CreateCanonicalId(resourceType, operatorKey, parameterName),
            ResourceType = resourceType,
            ResourceName = resourceType == "camera_binding" ? "相机绑定" : "模型资源",
            ResourceKey = $"{operatorId}.{parameterName}",
            OperatorKey = operatorKey,
            OperatorId = operatorId,
            OperatorType = "ImageAcquisition",
            OperatorIndex = 0,
            ParameterName = parameterName,
            Status = VisionAgentResourceStatuses.Pending,
            BlockingScope = VisionAgentResourceBlockingScopes.DeployRun,
            ResolutionTarget = VisionAgentResourceResolutionTargets.CameraSettings,
            DraftPolicy = VisionAgentResourceDraftPolicies.DraftAllowed,
            Description = "请选择权威资源。",
            Source = "operator_contract"
        };
        var primary = Missing("camera_binding", "imageacquisition#1", "acquire_1", "CameraBindingId");
        var secondary = Missing("camera_binding", "imageacquisition#2", "acquire_2", "SecondaryCameraBindingId");
        var unsupported = Missing("model_resource", "imageacquisition#3", "acquire_3", "ModelId");
        var seeded = host.ConversationService.TryUpdateWorkspaceSnapshot(sessionId, new VisionAgentWorkspaceSnapshotUpdate
        {
            ExpectedRevision = 0,
            ClientMutationId = Guid.NewGuid().ToString("D"),
            LifecycleState = "resources_pending",
            MissingResources = [primary, secondary, unsupported]
        });
        seeded.Success.Should().BeTrue();

        using var candidates = await host.Client.GetAsync("/api/ai/resource-candidates/camera-bindings");
        candidates.StatusCode.Should().Be(HttpStatusCode.OK);
        using var candidatesDocument = JsonDocument.Parse(await candidates.Content.ReadAsStringAsync());
        candidatesDocument.RootElement.EnumerateArray().Should().Contain(item =>
            item.GetProperty("id").GetString() == "camera-binding-01" &&
            item.GetProperty("displayName").GetString() == "Line camera" &&
            item.GetProperty("isEnabled").GetBoolean());
        var candidateFields = candidatesDocument.RootElement[0].EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        candidateFields.Should().BeEquivalentTo("id", "displayName", "isEnabled");

        using var clientRevision = await host.Client.PostAsJsonAsync($"/api/ai/sessions/{sessionId}/workspace-snapshot", new
        {
            expectedRevision = seeded.Revision,
            clientMutationId = Guid.NewGuid().ToString("D"),
            resourceRevision = 1,
            resourceDecisions = new[] { new { canonicalId = primary.CanonicalId, resourceKey = "camera-binding-01" } }
        });
        clientRevision.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await clientRevision.Content.ReadAsStringAsync()).Should().Contain("resource_revision_server_managed");

        using var unknown = await host.Client.PostAsJsonAsync($"/api/ai/sessions/{sessionId}/workspace-snapshot", new
        {
            expectedRevision = seeded.Revision,
            clientMutationId = Guid.NewGuid().ToString("D"),
            resourceDecisions = new[] { new { canonicalId = primary.CanonicalId, resourceKey = "missing-camera" } }
        });
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain("resource_not_found");

        using var disabled = await host.Client.PostAsJsonAsync($"/api/ai/sessions/{sessionId}/workspace-snapshot", new
        {
            expectedRevision = seeded.Revision,
            clientMutationId = Guid.NewGuid().ToString("D"),
            resourceDecisions = new[] { new { canonicalId = primary.CanonicalId, resourceKey = "camera-binding-disabled" } }
        });
        disabled.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await disabled.Content.ReadAsStringAsync()).Should().Contain("resource_disabled");

        using var unsupportedResponse = await host.Client.PostAsJsonAsync($"/api/ai/sessions/{sessionId}/workspace-snapshot", new
        {
            expectedRevision = seeded.Revision,
            clientMutationId = Guid.NewGuid().ToString("D"),
            resourceDecisions = new[] { new { canonicalId = unsupported.CanonicalId, resourceKey = "camera-binding-01" } }
        });
        unsupportedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unsupportedResponse.Content.ReadAsStringAsync()).Should().Contain("resource_type_unsupported");

        var firstMutationId = Guid.NewGuid().ToString("D");
        using var accepted = await host.Client.PostAsJsonAsync($"/api/ai/sessions/{sessionId}/workspace-snapshot", new
        {
            expectedRevision = seeded.Revision,
            clientMutationId = firstMutationId,
            lifecycleState = "build_inputs_changed",
            resourceDecisions = new[]
            {
                new
                {
                    canonicalId = primary.CanonicalId,
                    resourceKey = "camera-binding-01",
                    valueSummary = @"C:\forged\camera.json"
                }
            }
        });
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        using var acceptedDocument = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        var snapshot = acceptedDocument.RootElement.GetProperty("snapshot");
        snapshot.GetProperty("resourceRevision").GetInt32().Should().Be(1);
        var acceptedRevision = snapshot.GetProperty("revision").GetInt64();
        var decision = snapshot.GetProperty("resourceDecisions")[0];
        decision.GetProperty("canonicalId").GetString().Should().Be(primary.CanonicalId);
        decision.GetProperty("resourceKey").GetString().Should().Be("camera-binding-01");
        decision.GetProperty("valueSummary").GetString().Should().Be("Line camera");
        decision.GetProperty("source").GetString().Should().Be(VisionAgentResourceAuthority.CameraBindingSource);

        using var responseLossRetry = await host.Client.PostAsJsonAsync($"/api/ai/sessions/{sessionId}/workspace-snapshot", new
        {
            expectedRevision = seeded.Revision,
            clientMutationId = firstMutationId,
            lifecycleState = "build_inputs_changed",
            resourceDecisions = new[] { new { canonicalId = primary.CanonicalId, resourceKey = "camera-binding-01" } }
        });
        responseLossRetry.StatusCode.Should().Be(HttpStatusCode.OK);
        using var retryDocument = JsonDocument.Parse(await responseLossRetry.Content.ReadAsStringAsync());
        retryDocument.RootElement.GetProperty("snapshot").GetProperty("revision").GetInt64().Should().Be(acceptedRevision);
        retryDocument.RootElement.GetProperty("snapshot").GetProperty("resourceRevision").GetInt32().Should().Be(1);

        using var second = await host.Client.PostAsJsonAsync($"/api/ai/sessions/{sessionId}/workspace-snapshot", new
        {
            expectedRevision = acceptedRevision,
            clientMutationId = Guid.NewGuid().ToString("D"),
            resourceDecisions = new[] { new { canonicalId = secondary.CanonicalId, resourceKey = "camera-binding-02" } }
        });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        using var secondDocument = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var secondSnapshot = secondDocument.RootElement.GetProperty("snapshot");
        secondSnapshot.GetProperty("resourceRevision").GetInt32().Should().Be(2);
        secondSnapshot.GetProperty("resourceDecisions").GetArrayLength().Should().Be(2);

        using var stale = await host.Client.PostAsJsonAsync($"/api/ai/sessions/{sessionId}/workspace-snapshot", new
        {
            expectedRevision = acceptedRevision,
            clientMutationId = Guid.NewGuid().ToString("D"),
            resourceDecisions = new[] { new { canonicalId = primary.CanonicalId, resourceKey = "camera-binding-02" } }
        });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var staleDocument = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        staleDocument.RootElement.GetProperty("latestSnapshot").GetProperty("resourceRevision").GetInt32().Should().Be(2);
    }

    [Fact(DisplayName = "AI Session Build parameter mutation preserves JSON scalars and rejects structured values")]
    public async Task AiSessionBuildParameters_ShouldPreserveNullEmptyAndScalarValues()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        using var create = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId = Guid.NewGuid() });
        using var createDocument = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var sessionId = createDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString()!;

        using var accepted = await host.Client.PostAsJsonAsync($"/api/ai/sessions/{sessionId}/workspace-snapshot", new
        {
            expectedRevision = 0,
            clientMutationId = Guid.NewGuid().ToString("D"),
            answerRevision = 1,
            buildParameterValues = new Dictionary<string, object?>
            {
                ["op.optional"] = null,
                ["op.empty"] = string.Empty,
                ["op.threshold"] = 128.5,
                ["op.enabled"] = true
            }
        });
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        using var acceptedDocument = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        var values = acceptedDocument.RootElement.GetProperty("snapshot").GetProperty("buildParameterValues");
        values.GetProperty("op.optional").ValueKind.Should().Be(JsonValueKind.Null);
        values.GetProperty("op.empty").GetString().Should().BeEmpty();
        values.GetProperty("op.threshold").GetDouble().Should().Be(128.5);
        values.GetProperty("op.enabled").GetBoolean().Should().BeTrue();

        using var structured = await host.Client.PostAsJsonAsync($"/api/ai/sessions/{sessionId}/workspace-snapshot", new
        {
            expectedRevision = 1,
            clientMutationId = Guid.NewGuid().ToString("D"),
            answerRevision = 2,
            buildParameterValues = new { invalid = new { nested = true } }
        });
        structured.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await structured.Content.ReadAsStringAsync()).Should().Contain("build_parameter_value_invalid");
    }

    [Fact(DisplayName = "Build revalidation enforces parameter contracts and fails closed without artifact evidence")]
    public async Task BuildRevalidator_ShouldEnforceParameterContractsAndFailClosedWithoutArtifactEvidence()
    {
        var flow = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "parameter-revalidation-flow",
            Operators =
            [
                new OperatorDto
                {
                    Id = Guid.NewGuid(),
                    Name = "Thresholding",
                    Type = OperatorType.Thresholding,
                    Metadata = new Dictionary<string, object?> { ["agentTempId"] = "threshold_1" },
                    Parameters =
                    [
                        new ParameterDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "Threshold",
                            DisplayName = "阈值",
                            DataType = "double",
                            Value = 128d,
                            MinValue = 0d,
                            MaxValue = 255d,
                            IsRequired = true
                        },
                        new ParameterDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "Label",
                            DisplayName = "标签",
                            DataType = "string",
                            Value = string.Empty,
                            IsRequired = false
                        },
                        new ParameterDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "Mode",
                            DisplayName = "模式",
                            DataType = "string",
                            Value = "fast",
                            IsRequired = true
                        }
                    ]
                }
            ]
        };
        var candidateFingerprint = ExecutionFlowIdentity.ComputeFlowHash(flow.ToEntity());

        VisionAgentParameterMapping Mapping(
            string name,
            string dataType,
            bool required,
            object? min = null,
            object? max = null,
            List<VisionAgentParameterOption>? options = null) => new()
        {
            CanonicalKey = $"threshold_1.{name}",
            TempId = "threshold_1",
            OperatorType = "Thresholding",
            OperatorDisplayName = "阈值",
            ParameterName = name,
            ParameterDisplayName = name,
            DataType = dataType,
            IsRequired = required,
            RequiredPolicy = required ? OperatorParameterRequiredPolicies.Required : OperatorParameterRequiredPolicies.Optional,
            Pending = true,
            MinValue = min,
            MaxValue = max,
            Options = options ?? []
        };

        VisionAgentPublicBuildResultV1 Build(VisionAgentParameterMapping mapping) => new()
        {
            RunId = "ar_parameter_revalidation",
            BuildId = "build_parameter_revalidation",
            ClientOperationId = Guid.NewGuid(),
            BuildIdentity = "build-identity",
            SubmittedBuildFingerprint = new string('a', 64),
            PlanId = "plan_parameter_revalidation",
            PlanHash = new string('b', 64),
            AnswerSetFingerprint = new string('c', 64),
            ProjectBaseline = new AiProjectBaselineIdentity(),
            CandidateFlowFingerprint = candidateFingerprint,
            OperatorCount = 1,
            ParameterMapping = [mapping],
            WorkflowDiff = new VisionAgentWorkflowDiff()
        };

        async Task<VisionAgentBuildRevalidationResult> Revalidate(
            VisionAgentParameterMapping mapping,
            object? value)
        {
            return await new VisionAgentBuildRevalidator().RevalidateAsync(new VisionAgentBuildRevalidationRequest
            {
                CandidateFlowJson = JsonSerializer.Serialize(flow),
                Build = Build(mapping),
                ParameterValues = new Dictionary<string, JsonElement>
                {
                    [mapping.CanonicalKey] = JsonSerializer.SerializeToElement(value)
                },
                AnswerRevision = 2,
                ResourceRevision = 0
            }, CancellationToken.None);
        }

        var threshold = Mapping("Threshold", "number", true, 0d, 255d);
        var valid = await Revalidate(threshold, 200d);
        valid.CandidateFlowFingerprint.Should().NotBe(candidateFingerprint);
        valid.Build.CandidateFlowFingerprint.Should().Be(valid.CandidateFlowFingerprint);
        ExecutionFlowIdentity.ComputeFlowHash(
                JsonSerializer.Deserialize<OperatorFlowDto>(
                    valid.CandidateFlowJson,
                    CaseInsensitiveWebJsonOptions)!.ToEntity())
            .Should().Be(valid.CandidateFlowFingerprint);
        valid.Build.ParameterMapping.Single().Pending.Should().BeFalse();
        valid.Build.ParameterMapping.Single().Value.Should().Be(200d);
        valid.Build.Validation.HandoffEligible.Should().BeFalse();
        valid.Build.Validation.ApplyGate.Blocked.Should().BeTrue();
        valid.Build.Validation.ApplyGate.ApplyBlockers.Should().Contain("route_semantics_evidence_missing");
        valid.Build.Validation.ApplyGate.ApplyBlockers.Should().Contain("artifact_fingerprint_inconsistent");

        var nullRequired = await Revalidate(threshold, null);
        nullRequired.Build.ParameterMapping.Single().Pending.Should().BeTrue();
        nullRequired.Build.WorkflowDiff.ValidationFailures.Should().Contain(item => item.Contains("required_value_missing"));

        var wrongType = await Revalidate(threshold, "200");
        wrongType.Build.WorkflowDiff.ValidationFailures.Should().Contain(item => item.Contains("number_required"));

        var outOfRange = await Revalidate(threshold, 300d);
        outOfRange.Build.WorkflowDiff.ValidationFailures.Should().Contain(item => item.Contains("above_maximum"));

        var label = Mapping("Label", "string", false);
        var emptyString = await Revalidate(label, string.Empty);
        emptyString.Build.ParameterMapping.Single().Pending.Should().BeFalse();
        emptyString.Build.ParameterMapping.Single().Value.Should().Be(string.Empty);

        var mode = Mapping("Mode", "string", true, options:
        [
            new VisionAgentParameterOption { Label = "快速", Value = "fast" },
            new VisionAgentParameterOption { Label = "精确", Value = "accurate" }
        ]);
        var invalidEnum = await Revalidate(mode, "unsupported");
        invalidEnum.Build.WorkflowDiff.ValidationFailures.Should().Contain(item => item.Contains("enum_value_invalid"));
    }

    [Fact(DisplayName = "Build revalidation projects canonical topology and clears trusted camera mapping blockers")]
    public async Task BuildRevalidator_ShouldProjectCanonicalTopologyAndApplyTrustedCameraAuthority()
    {
        var planHash = new string('b', 64);
        const string catalogVersion = "catalog.revalidation.v1";
        const string buildIntent = "new";
        const string taskType = "presence_detection";
        Dictionary<string, object?> ArtifactMetadata(string tempId) => new(StringComparer.OrdinalIgnoreCase)
        {
            ["agentTempId"] = tempId,
            ["agentPlanHash"] = planHash,
            ["agentCatalogVersion"] = catalogVersion,
            ["agentBuildIntent"] = buildIntent,
            ["agentTaskType"] = taskType
        };
        var contractCatalog = new VisionAgentOperatorContractCatalog();
        void CompleteContractProjection(OperatorDto op)
        {
            contractCatalog.TryGet(op.Type.ToString(), out var contract).Should().BeTrue();
            foreach (var input in contract.InputPorts)
            {
                var port = op.InputPorts.FirstOrDefault(item =>
                    item.Name.Equals(input.Name, StringComparison.OrdinalIgnoreCase));
                if (port == null)
                {
                    op.InputPorts.Add(new PortDto
                    {
                        Id = Guid.NewGuid(),
                        Name = input.Name,
                        Direction = PortDirection.Input,
                        DataType = input.DataType,
                        IsRequired = input.IsRequired
                    });
                }
                else
                {
                    port.DataType = input.DataType;
                    port.IsRequired = input.IsRequired;
                }
            }

            foreach (var output in contract.OutputPorts)
            {
                var port = op.OutputPorts.FirstOrDefault(item =>
                    item.Name.Equals(output.Name, StringComparison.OrdinalIgnoreCase));
                if (port == null)
                {
                    op.OutputPorts.Add(new PortDto
                    {
                        Id = Guid.NewGuid(),
                        Name = output.Name,
                        Direction = PortDirection.Output,
                        DataType = output.DataType
                    });
                }
                else
                {
                    port.DataType = output.DataType;
                }
            }

            foreach (var parameterContract in contract.Parameters)
            {
                var parameter = op.Parameters.FirstOrDefault(item =>
                    item.Name.Equals(parameterContract.Name, StringComparison.OrdinalIgnoreCase));
                if (parameter == null)
                {
                    parameter = new ParameterDto
                    {
                        Id = Guid.NewGuid(),
                        Name = parameterContract.Name
                    };
                    op.Parameters.Add(parameter);
                }

                parameter.DisplayName = parameterContract.DisplayName;
                parameter.Description = parameterContract.Description;
                parameter.DataType = parameterContract.DataType;
                parameter.DefaultValue = parameterContract.DefaultValue;
                parameter.MinValue = parameterContract.MinValue;
                parameter.MaxValue = parameterContract.MaxValue;
                parameter.IsRequired = parameterContract.IsRequired;
                parameter.Options = parameterContract.Options?.Select(option => new ParameterOption
                {
                    Label = option.Label,
                    Value = option.Value
                }).ToList();
            }
        }

        var cameraId = Guid.NewGuid();
        var cameraOutputId = Guid.NewGuid();
        var thresholdId = Guid.NewGuid();
        var thresholdInputId = Guid.NewGuid();
        var thresholdOutputId = Guid.NewGuid();
        var resultOutputId = Guid.NewGuid();
        var resultInputId = Guid.NewGuid();
        var flow = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "camera-authority-revalidation-flow",
            Operators =
            [
                new OperatorDto
                {
                    Id = cameraId,
                    Name = "Image acquisition",
                    Type = OperatorType.ImageAcquisition,
                    Metadata = ArtifactMetadata("op_cam"),
                    OutputPorts =
                    [
                        new PortDto
                        {
                            Id = cameraOutputId,
                            Name = "Image",
                            Direction = PortDirection.Output,
                            DataType = PortDataType.Image
                        }
                    ],
                    Parameters =
                    [
                        new ParameterDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "SourceType",
                            DisplayName = "采集来源",
                            DataType = "string",
                            Value = "Camera",
                            IsRequired = true
                        },
                        new ParameterDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "CameraId",
                            DisplayName = "相机绑定",
                            DataType = "string",
                            Value = "<pending-camera-binding>",
                            IsRequired = true
                        }
                    ]
                },
                new OperatorDto
                {
                    Id = thresholdId,
                    Name = "Thresholding",
                    Type = OperatorType.Thresholding,
                    Metadata = ArtifactMetadata("op_threshold"),
                    InputPorts =
                    [
                        new PortDto
                        {
                            Id = thresholdInputId,
                            Name = "Image",
                            Direction = PortDirection.Input,
                            DataType = PortDataType.Image,
                            IsRequired = true
                        }
                    ],
                    OutputPorts =
                    [
                        new PortDto
                        {
                            Id = thresholdOutputId,
                            Name = "Image",
                            Direction = PortDirection.Output,
                            DataType = PortDataType.Image
                        }
                    ],
                    Parameters =
                    [
                        new ParameterDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "Threshold",
                            DisplayName = "阈值",
                            DataType = "double",
                            Value = 128d,
                            MinValue = 0d,
                            MaxValue = 255d,
                            IsRequired = true
                        }
                    ]
                },
                new OperatorDto
                {
                    Id = resultOutputId,
                    Name = "Result output",
                    Type = OperatorType.ResultOutput,
                    Metadata = ArtifactMetadata("op_result"),
                    InputPorts =
                    [
                        new PortDto
                        {
                            Id = resultInputId,
                            Name = "Image",
                            Direction = PortDirection.Input,
                            DataType = PortDataType.Image,
                            IsRequired = false
                        }
                    ]
                }
            ],
            Connections =
            [
                new OperatorConnectionDto
                {
                    Id = Guid.NewGuid(),
                    SourceOperatorId = cameraId,
                    SourcePortId = cameraOutputId,
                    TargetOperatorId = thresholdId,
                    TargetPortId = thresholdInputId
                },
                new OperatorConnectionDto
                {
                    Id = Guid.NewGuid(),
                    SourceOperatorId = thresholdId,
                    SourcePortId = thresholdOutputId,
                    TargetOperatorId = resultOutputId,
                    TargetPortId = resultInputId
                }
            ]
        };
        foreach (var op in flow.Operators)
        {
            CompleteContractProjection(op);
        }
        var candidateFlowJson = JsonSerializer.Serialize(flow);
        var candidateFingerprint = ExecutionFlowIdentity.ComputeFlowHash(
            JsonSerializer.Deserialize<OperatorFlowDto>(
                candidateFlowJson,
                CaseInsensitiveWebJsonOptions)!.ToEntity());
        const string cameraResourceKey = "f06-evidence-camera-01";
        var canonicalId = VisionAgentResourceIdentity.CreateCanonicalId(
            "camera_binding", "imageacquisition#1", "CameraId", cameraResourceKey);
        var mapping = new VisionAgentParameterMapping
        {
            CanonicalKey = "op_cam.CameraId",
            TempId = "op_cam",
            OperatorType = "ImageAcquisition",
            OperatorDisplayName = "图像采集",
            ParameterName = "CameraId",
            ParameterDisplayName = "相机绑定",
            DataType = "camerabinding",
            IsRequired = true,
            Value = "<pending-camera-binding>",
            HasExplicitValue = true,
            ValueSummary = "等待绑定",
            Source = "pending_metadata",
            Pending = true,
            ResourceKind = "camera_binding",
            ResourceCanonicalId = canonicalId,
            ResourceDependent = true
        };
        var missingResource = new AiMissingResourceInfo
        {
            CanonicalId = canonicalId,
            ResourceType = "camera_binding",
            ResourceName = "相机绑定",
            ResourceKey = "op_cam.CameraId",
            OperatorKey = "imageacquisition#1",
            OperatorId = "op_cam",
            OperatorType = "ImageAcquisition",
            OperatorIndex = 0,
            ParameterName = "CameraId",
            Status = VisionAgentResourceStatuses.Pending,
            Source = "operator_contract"
        };
        var revalidation = await new VisionAgentBuildRevalidator().RevalidateAsync(
            new VisionAgentBuildRevalidationRequest
            {
                CandidateFlowJson = candidateFlowJson,
                Build = new VisionAgentPublicBuildResultV1
                {
                    RunId = "ar_camera_authority_revalidation",
                    BuildId = "build_camera_authority_revalidation",
                    BuildIdentity = "build-identity",
                    PlanId = "plan_camera_authority_revalidation",
                    PlanHash = planHash,
                    AnswerSetFingerprint = new string('c', 64),
                    CandidateFlowFingerprint = candidateFingerprint,
                    OperatorCount = 3,
                    ConnectionCount = 2,
                    ParameterMapping = [mapping],
                    MissingResources = [missingResource],
                    WorkflowDiff = new VisionAgentWorkflowDiff
                    {
                        PendingParameters = [mapping.CanonicalKey],
                        MissingResources = [canonicalId],
                        DeploymentBlockers = [$"parameter:{mapping.CanonicalKey}", $"resource:{canonicalId}"]
                    }
                },
                ResourceDecisions =
                [
                    new VisionAgentResourceDecision
                    {
                        CanonicalId = canonicalId,
                        Status = VisionAgentResourceStatuses.Bound,
                        ResourceKey = cameraResourceKey,
                        ResourceType = "camera_binding",
                        OperatorKey = "imageacquisition#1",
                        OperatorId = "op_cam",
                        OperatorType = "ImageAcquisition",
                        OperatorIndex = 0,
                        ParameterName = "CameraId",
                        ValueSummary = "F06 evidence camera",
                        Source = VisionAgentResourceAuthority.CameraBindingSource
                    }
                ],
                AnswerRevision = 2,
                ResourceRevision = 1
            },
            CancellationToken.None);

        var result = revalidation.Build;
        var applied = result.ParameterMapping.Should().ContainSingle().Subject;
        applied.Pending.Should().BeFalse();
        applied.Value.Should().Be(cameraResourceKey);
        applied.ValueSummary.Should().Be("F06 evidence camera");
        applied.Source.Should().Be(VisionAgentResourceAuthority.CameraBindingSource);
        result.MissingResources.Should().BeEmpty();
        result.WorkflowDiff.PendingParameters.Should().BeEmpty();
        result.WorkflowDiff.DeploymentBlockers.Should().BeEmpty();
        result.Validation.Structural.BlockerCount.Should().Be(0);
        result.Validation.DryRun.BlockerCount.Should().Be(0);
        result.Validation.ApplyGate.CompiledFingerprint.Should().NotBeNullOrWhiteSpace();
        result.Validation.ApplyGate.ValidationFingerprint.Should().Be(result.Validation.ApplyGate.CompiledFingerprint);
        result.Validation.ApplyGate.DryRunFingerprint.Should().Be(result.Validation.ApplyGate.CompiledFingerprint);
        result.Validation.ApplyGate.PrecheckFingerprint.Should().Be(result.Validation.ApplyGate.CompiledFingerprint);
        result.Validation.ApplyGate.ReturnedFlowSemanticFingerprint.Should().Be(result.Validation.ApplyGate.CompiledFingerprint);
        result.Validation.ApplyGate.ApplyBlockers.Should().BeEmpty();
        result.Validation.ApplyGate.DeploymentBlockers.Should().BeEmpty();
        result.Validation.Manifest.Status.Should().Be("passed");
        result.Validation.HandoffEligible.Should().BeTrue();
        result.Validation.ApplyGate.Blocked.Should().BeFalse();
        result.Validation.ApplyGate.ArtifactFingerprintConsistent.Should().BeTrue();
        result.Validation.ApplyGate.RouteSemanticsSatisfied.Should().BeTrue();

        revalidation.CandidateFlowFingerprint.Should().Be(result.CandidateFlowFingerprint);
        revalidation.CandidateFlowFingerprint.Should().NotBe(candidateFingerprint);
        var candidateFlow = JsonSerializer.Deserialize<OperatorFlowDto>(
            revalidation.CandidateFlowJson,
            CaseInsensitiveWebJsonOptions)!;
        ExecutionFlowIdentity.ComputeFlowHash(candidateFlow.ToEntity())
            .Should().Be(revalidation.CandidateFlowFingerprint);
        var candidateCamera = candidateFlow.Operators.Single(op => op.Id == cameraId);
        candidateCamera.Parameters.Single(parameter => parameter.Name == "CameraId")
            .Value.Should().BeOfType<JsonElement>().Which.GetString().Should().Be(cameraResourceKey);
        candidateFlow.Operators.Should().OnlyContain(op =>
            JsonSerializer.SerializeToElement(
                op.Metadata!["agentArtifactFingerprint"],
                (JsonSerializerOptions?)null).GetString() ==
            result.Validation.ApplyGate.CompiledFingerprint);
        candidateFlow.Operators.Should().OnlyContain(op =>
            JsonSerializer.SerializeToElement(
                op.Metadata!["agentRouteSemanticsSatisfied"],
                (JsonSerializerOptions?)null).GetBoolean());
    }

    [Fact(DisplayName = "AI operation identity replays matching requests rejects conflicts and supports lookup")]
    public async Task AiOperationIdentity_ShouldReplayRejectConflictAndSupportLookup()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var clientOperationId = Guid.NewGuid();
        using var first = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        using var firstDocument = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var sessionId = firstDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString();

        using var replay = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId });
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        using var replayDocument = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        replayDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString().Should().Be(sessionId);
        replayDocument.RootElement.GetProperty("operation").GetProperty("status").GetString()
            .Should().Be(AiOperationStatuses.Created);

        using var conflict = await host.Client.PostAsJsonAsync("/api/ai/sessions", new
        {
            clientOperationId,
            projectId = Guid.Empty
        });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var conflictDocument = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        conflictDocument.RootElement.GetProperty("errorCode").GetString().Should().Be("operation_identity_conflict");

        using var lookup = await host.Client.GetAsync(
            $"/api/ai/operations/{clientOperationId:D}?kind={AiOperationKinds.SessionCreate}");
        lookup.StatusCode.Should().Be(HttpStatusCode.OK);
        using var lookupDocument = JsonDocument.Parse(await lookup.Content.ReadAsStringAsync());
        lookupDocument.RootElement.GetProperty("sessionId").GetString().Should().Be(sessionId);
        lookupDocument.RootElement.GetProperty("payloadFingerprint").GetString().Should().StartWith("sha256:");

        using var list = await host.Client.GetAsync("/api/ai/sessions");
        using var listDocument = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        listDocument.RootElement.GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact(DisplayName = "Build terminal replay exposes only the G3 public DTO")]
    public async Task BuildTerminalReplay_ShouldExposeRedactedPublicBuildResult()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var candidate = await CreateG3BuildCandidateAsync(host);

        using var response = await host.Client.GetAsync($"/api/ai/agent-runs/{candidate.RunId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        JsonElement publicBuild = default;
        foreach (var evt in document.RootElement.GetProperty("events").EnumerateArray().Reverse())
        {
            var payload = evt.GetProperty("payload");
            if (payload.TryGetProperty("publicBuildResult", out publicBuild)) break;
            if (payload.TryGetProperty("diagnostic", out var diagnostic) &&
                diagnostic.TryGetProperty("publicBuildResult", out publicBuild)) break;
        }
        publicBuild.ValueKind.Should().Be(JsonValueKind.Object);
        var expectedKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "schemaVersion", "runId", "buildId", "clientOperationId", "buildIdentity",
            "submittedBuildFingerprint", "planId", "planHash", "answerSetFingerprint",
            "answerRevision", "resourceRevision", "projectBaseline", "candidateFlowFingerprint",
            "operatorCount", "connectionCount", "operatorPipeline", "parameterMapping",
            "missingResources", "workflowDiff", "validation", "publicTimeline", "publicWarnings",
            "metadataOnly", "redactionPass"
        };
        publicBuild.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(expectedKeys);
        publicBuild.GetProperty("runId").GetString().Should().Be(candidate.RunId);
        publicBuild.GetProperty("validation").GetProperty("applyGate").ValueKind.Should().Be(JsonValueKind.Object);
        publicBuild.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
        publicBuild.GetProperty("redactionPass").GetBoolean().Should().BeTrue();

        var normalized = publicBuild.GetRawText().ToLowerInvariant();
        normalized.Should().NotContain("\"flow\"");
        normalized.Should().NotContain("existingflowjson");
        normalized.Should().NotContain("currentcanvasflowjson");
        normalized.Should().NotContain("tooltrace");
        normalized.Should().NotContain("rawprompt");
        normalized.Should().NotContain("reasoningcontent");
    }

    [Fact(DisplayName = "Handoff create is canonical idempotent redacted and consumed in two phases")]
    public async Task HandoffCreateAndConsume_ShouldPreserveCanonicalAuthority()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(
            handler: HandoffReadyBuildResultAsync,
            useAuth: true,
            planHandler: HandoffReadyPlanAsync);
        host.AuthorizeAs("owner-a-token");
        var candidate = await CreateHandoffReadyCandidateAsync(host);
        var handoffOperationId = Guid.NewGuid();
        object CreateRequest(string? candidateFingerprint = null) => new
        {
            clientOperationId = handoffOperationId,
            sessionId = candidate.SessionId,
            expectedSessionRevision = candidate.Revision,
            planRunId = candidate.PlanRunId,
            planId = candidate.Build.PlanId,
            planHash = candidate.Build.PlanHash,
            buildRunId = candidate.BuildRunId,
            buildClientOperationId = candidate.BuildClientOperationId,
            buildIdentity = candidate.Build.BuildIdentity,
            candidateFlowFingerprint = candidateFingerprint ?? candidate.Build.CandidateFlowFingerprint,
            answerRevision = candidate.Build.AnswerRevision,
            resourceRevision = candidate.Build.ResourceRevision,
            projectBaseline = new { targetKind = "new" }
        };

        using var created = await host.Client.PostAsJsonAsync("/api/ai/handoffs", CreateRequest());
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdJson = await created.Content.ReadAsStringAsync();
        using var createdDocument = JsonDocument.Parse(createdJson);
        var artifact = createdDocument.RootElement;
        var artifactId = artifact.GetProperty("artifactId").GetString()!;
        artifact.GetProperty("status").GetString().Should().Be(AiWorkspaceHandoffStatuses.Available);
        artifact.GetProperty("targetKind").GetString().Should().Be("new");
        artifact.GetProperty("candidateFlow").GetProperty("operators").GetArrayLength().Should().Be(1);
        artifact.GetProperty("build").GetProperty("validation").GetProperty("handoffEligible")
            .GetBoolean().Should().BeTrue();
        createdJson.Should().NotContain("ownerHash");
        createdJson.Should().NotContain("systemPrompt");
        createdJson.ToLowerInvariant().Should().NotContain("reasoning");
        createdJson.ToLowerInvariant().Should().NotContain("authorization");

        using var replay = await host.Client.PostAsJsonAsync("/api/ai/handoffs", CreateRequest());
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        using var conflict = await host.Client.PostAsJsonAsync(
            "/api/ai/handoffs",
            CreateRequest(new string('F', 64)));
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var lookupByBuild = await host.Client.GetAsync(
            $"/api/ai/handoffs/by-build/{candidate.BuildRunId}");
        lookupByBuild.StatusCode.Should().Be(HttpStatusCode.OK);
        (await lookupByBuild.Content.ReadAsStringAsync()).Should().Contain(artifactId);

        var consumeOperationId = Guid.NewGuid();
        var consume = new
        {
            clientOperationId = consumeOperationId,
            targetProjectId = (Guid?)null,
            candidateFlowFingerprint = candidate.Build.CandidateFlowFingerprint
        };
        using var reserved = await host.Client.PostAsJsonAsync(
            $"/api/ai/handoffs/{artifactId}/consume", consume);
        reserved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reserved.Content.ReadAsStringAsync()).Should().Contain("consuming");

        using var competing = await host.Client.PostAsJsonAsync(
            $"/api/ai/handoffs/{artifactId}/consume",
            new
            {
                clientOperationId = Guid.NewGuid(),
                targetProjectId = (Guid?)null,
                candidateFlowFingerprint = candidate.Build.CandidateFlowFingerprint
            });
        competing.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var acknowledged = await host.Client.PostAsJsonAsync(
            $"/api/ai/handoffs/{artifactId}/acknowledge", consume);
        using var acknowledgeReplay = await host.Client.PostAsJsonAsync(
            $"/api/ai/handoffs/{artifactId}/acknowledge", consume);
        acknowledged.StatusCode.Should().Be(HttpStatusCode.OK);
        acknowledgeReplay.StatusCode.Should().Be(HttpStatusCode.OK);
        var acknowledgedJson = await acknowledged.Content.ReadAsStringAsync();
        acknowledgedJson.Should().Contain("consumed");
        acknowledgedJson.Should().Contain("\"projectSaved\":false");
    }

    [Theory(DisplayName = "Handoff candidate public policy distinguishes schema identities from private values")]
    [InlineData(
        "{\"id\":\"aaaaaaaa-d123-4aaa-8aaa-aaaaaaaaaaaa\",\"operators\":[{\"parameters\":[{\"name\":\"FilePath\",\"displayName\":\"文件路径\",\"value\":\"\"}]}]}",
        true)]
    [InlineData("{\"operators\":[{\"parameters\":[{\"name\":\"FilePath\",\"value\":\"C:\\\\factory\\\\secret.png\"}]}]}", false)]
    [InlineData("{\"operators\":[{\"parameters\":[{\"name\":\"CameraAddress\",\"value\":\"192.168.1.8\"}]}]}", false)]
    [InlineData("{\"operators\":[{\"parameters\":[{\"name\":\"PlcRegister\",\"value\":\"D100\"}]}]}", false)]
    [InlineData("{\"operators\":[],\"apiKey\":\"not-public\"}", false)]
    public void HandoffCandidatePublicPolicy_ShouldFailClosedWithoutRejectingGuidSegments(
        string candidateJson,
        bool expected)
    {
        var actual = AiWorkspaceHandoffEndpoints.IsPublicCandidate(candidateJson, out var message);

        actual.Should().Be(expected);
        message.Should().Be(expected ? string.Empty :
            "候选流程包含 secret、私有路径、地址、附件或非 public 状态，不能创建交接工件。");
    }

    [Fact(DisplayName = "Handoff endpoints enforce authentication role and owner scope")]
    public async Task HandoffLookup_ShouldEnforceRoleAndOwner()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(
            handler: HandoffReadyBuildResultAsync,
            useAuth: true,
            planHandler: HandoffReadyPlanAsync);
        host.AuthorizeAs("owner-a-token");
        var candidate = await CreateHandoffReadyCandidateAsync(host);
        using var created = await host.Client.PostAsJsonAsync("/api/ai/handoffs", new
        {
            clientOperationId = Guid.NewGuid(),
            sessionId = candidate.SessionId,
            expectedSessionRevision = candidate.Revision,
            planRunId = candidate.PlanRunId,
            planId = candidate.Build.PlanId,
            planHash = candidate.Build.PlanHash,
            buildRunId = candidate.BuildRunId,
            buildClientOperationId = candidate.BuildClientOperationId,
            buildIdentity = candidate.Build.BuildIdentity,
            candidateFlowFingerprint = candidate.Build.CandidateFlowFingerprint,
            answerRevision = candidate.Build.AnswerRevision,
            resourceRevision = candidate.Build.ResourceRevision,
            projectBaseline = new { targetKind = "new" }
        });
        using var document = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var artifactId = document.RootElement.GetProperty("artifactId").GetString();

        host.AuthorizeAs("owner-b-token");
        using var nonOwner = await host.Client.GetAsync($"/api/ai/handoffs/{artifactId}");
        nonOwner.StatusCode.Should().Be(HttpStatusCode.NotFound);

        host.AuthorizeAs("operator-token");
        using var operatorResponse = await host.Client.GetAsync($"/api/ai/handoffs/{artifactId}");
        operatorResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var anonymous = await host.CreateAnonymousClient().GetAsync($"/api/ai/handoffs/{artifactId}");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Build revalidation binds run session candidate revisions and persists the latest ApplyGate")]
    public async Task BuildRevalidation_ShouldEnforceIdentityAndPersistReadiness()
    {
        string? emittedCanvasFlowJson = null;
        string? emittedCandidateFingerprint = null;
        await using var host = await AgentRunEndpointTestHost.CreateAsync(
            useAuth: true,
            revalidateHandler: (request, _) =>
            {
                var flow = JsonSerializer.Deserialize<OperatorFlowDto>(request.CandidateFlowJson)!;
                flow.DecisionConfiguration = new ClearVision.Product.Core.Decisions.DecisionConfiguration
                {
                    MissingDecisionPolicy = ClearVision.Product.Core.Decisions.MissingDecisionPolicy.Invalid
                };
                emittedCanvasFlowJson = JsonSerializer.Serialize(flow);
                emittedCandidateFingerprint = ExecutionFlowIdentity.ComputeFlowHash(flow.ToEntity());
                var build = request.Build with
                {
                    AnswerRevision = request.AnswerRevision,
                    ResourceRevision = request.ResourceRevision,
                    CandidateFlowFingerprint = emittedCandidateFingerprint,
                    Validation = request.Build.Validation with
                    {
                        HandoffEligible = true,
                        ReadinessStatus = "ready",
                        FirstFixRecommendation = string.Empty,
                        ApplyGate = request.Build.Validation.ApplyGate with
                        {
                            CanvasApplyReady = true,
                            RuntimeDraftReady = true,
                            Blocked = false,
                            Status = "ready_for_handoff",
                            ApplyBlockers = []
                        }
                    }
                };
                return Task.FromResult(new VisionAgentBuildRevalidationResult
                {
                    Build = build,
                    CandidateFlowJson = emittedCanvasFlowJson
                });
            });
        host.AuthorizeAs("owner-a-token");
        var candidate = await CreateG3BuildCandidateAsync(host);
        var initialSession = host.ConversationService.GetSession(candidate.SessionId)!;
        var initialCanvasFlowJson = initialSession.CurrentCanvasFlowJson;
        initialCanvasFlowJson.Should().NotBeNullOrWhiteSpace();
        object Command(
            string? sessionId = null,
            string? buildId = null,
            string? fingerprint = null,
            long? revision = null,
            int? answerRevision = null,
            int? resourceRevision = null) => new
        {
            sessionId = sessionId ?? candidate.SessionId,
            expectedRevision = revision ?? candidate.Revision,
            clientMutationId = Guid.NewGuid(),
            buildId = buildId ?? candidate.Build.BuildId,
            candidateFlowFingerprint = fingerprint ?? candidate.Build.CandidateFlowFingerprint,
            answerRevision = answerRevision ?? candidate.Build.AnswerRevision,
            resourceRevision = resourceRevision ?? candidate.Build.ResourceRevision
        };

        using var wrongSession = await host.Client.PostAsJsonAsync(
            $"/api/ai/agent-runs/{candidate.RunId}/revalidate", Command(sessionId: "other_session"));
        wrongSession.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var wrongBuild = await host.Client.PostAsJsonAsync(
            $"/api/ai/agent-runs/{candidate.RunId}/revalidate", Command(buildId: "other_build"));
        using var wrongCandidate = await host.Client.PostAsJsonAsync(
            $"/api/ai/agent-runs/{candidate.RunId}/revalidate", Command(fingerprint: new string('f', 64)));
        using var wrongRevision = await host.Client.PostAsJsonAsync(
            $"/api/ai/agent-runs/{candidate.RunId}/revalidate", Command(revision: candidate.Revision + 1));
        using var wrongAnswer = await host.Client.PostAsJsonAsync(
            $"/api/ai/agent-runs/{candidate.RunId}/revalidate", Command(answerRevision: candidate.Build.AnswerRevision + 1));
        using var wrongResource = await host.Client.PostAsJsonAsync(
            $"/api/ai/agent-runs/{candidate.RunId}/revalidate", Command(resourceRevision: candidate.Build.ResourceRevision + 1));
        foreach (var conflict in new[] { wrongBuild, wrongCandidate, wrongRevision, wrongAnswer, wrongResource })
        {
            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await conflict.Content.ReadAsStringAsync()).Should().Contain("build_revalidation_stale");
        }

        var otherRunId = await host.CreateRunAsync("Other owned Build run");
        using var wrongRun = await host.Client.PostAsJsonAsync(
            $"/api/ai/agent-runs/{otherRunId}/revalidate", Command());
        wrongRun.StatusCode.Should().Be(HttpStatusCode.Conflict);

        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = () =>
            throw new IOException("revalidation primary failed");
        using var persistenceFailure = await host.Client.PostAsJsonAsync(
            $"/api/ai/agent-runs/{candidate.RunId}/revalidate", Command());
        host.ConcreteConversationService.PrimaryStoreWriteFaultInjector = null;
        persistenceFailure.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await persistenceFailure.Content.ReadAsStringAsync()).Should().Contain("session_persistence_failed");
        var sessionAfterFailure = host.ConversationService.GetSession(candidate.SessionId)!;
        sessionAfterFailure.WorkspaceSnapshot!.Revision.Should().Be(candidate.Revision);
        sessionAfterFailure.WorkspaceSnapshot.PublicBuildResult!.CandidateFlowFingerprint
            .Should().Be(candidate.Build.CandidateFlowFingerprint);
        sessionAfterFailure.CurrentCanvasFlowJson.Should().Be(initialCanvasFlowJson);

        using var accepted = await host.Client.PostAsJsonAsync(
            $"/api/ai/agent-runs/{candidate.RunId}/revalidate", Command());
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        using var acceptedDocument = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        var root = acceptedDocument.RootElement;
        root.GetProperty("build").GetProperty("candidateFlowFingerprint").GetString()
            .Should().Be(emittedCandidateFingerprint)
            .And.NotBe(candidate.Build.CandidateFlowFingerprint);
        root.GetProperty("build").GetProperty("validation").GetProperty("handoffEligible").GetBoolean().Should().BeTrue();
        root.GetProperty("build").GetProperty("validation").GetProperty("applyGate")
            .GetProperty("status").GetString().Should().Be("ready_for_handoff");
        root.GetProperty("snapshot").GetProperty("revision").GetInt64().Should().Be(candidate.Revision + 1);
        root.GetProperty("snapshot").GetProperty("lifecycleState").GetString().Should().Be("build_ready");
        var committedSession = host.ConversationService.GetSession(candidate.SessionId)!;
        committedSession.CurrentCanvasFlowJson.Should().Be(emittedCanvasFlowJson);
        committedSession.WorkspaceSnapshot!.PublicBuildResult!.CandidateFlowFingerprint
            .Should().Be(emittedCandidateFingerprint);
        ExecutionFlowIdentity.ComputeFlowHash(
                JsonSerializer.Deserialize<OperatorFlowDto>(committedSession.CurrentCanvasFlowJson!)!.ToEntity())
            .Should().Be(emittedCandidateFingerprint);

        using var staleRetry = await host.Client.PostAsJsonAsync(
            $"/api/ai/agent-runs/{candidate.RunId}/revalidate", Command());
        staleRetry.StatusCode.Should().Be(HttpStatusCode.Conflict);
        host.ConversationService.GetSession(candidate.SessionId)!.CurrentCanvasFlowJson
            .Should().Be(emittedCanvasFlowJson);

        host.AuthorizeAs("owner-b-token");
        using var nonOwner = await host.Client.PostAsJsonAsync(
            $"/api/ai/agent-runs/{candidate.RunId}/revalidate", Command(revision: candidate.Revision + 1));
        nonOwner.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "Build operation identity includes answer parameter and resource revisions")]
    public async Task BuildOperationIdentity_ShouldReplayAndRejectInputRevisionConflicts()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        using var sessionResponse = await host.Client.PostAsJsonAsync("/api/ai/sessions", new
        {
            clientOperationId = Guid.NewGuid()
        });
        using var sessionDocument = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        var sessionId = sessionDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString()!;
        var operationId = Guid.NewGuid();
        var buildFromPlan = BuildableAgentRunBuildFromPlanRequest() with
        {
            WorkspaceExpectedRevision = 0,
            AnswerRevision = 0,
            ResourceRevision = 0
        };
        var request = new AgentRunCreateRequest
        {
            ClientOperationId = operationId,
            Target = new AiProjectTargetRequest { TargetKind = "new" },
            Description = "Idempotent G3 Build",
            SessionId = sessionId,
            BuildFromPlan = buildFromPlan,
            UseVisionAgentGenerateFlow = true
        };

        using var first = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", request);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        using var firstDocument = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var runId = firstDocument.RootElement.GetProperty("runId").GetString();

        using var replay = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", request);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        using var replayDocument = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        replayDocument.RootElement.GetProperty("runId").GetString().Should().Be(runId);

        using var conflict = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", request with
        {
            BuildFromPlan = buildFromPlan with
            {
                AnswerRevision = 1,
                ResourceRevision = 1,
                ParameterValues = new Dictionary<string, JsonElement>
                {
                    ["threshold_1.threshold"] = JsonSerializer.SerializeToElement(128)
                },
                ResourceDecisions =
                [
                    new VisionAgentResourceDecision
                    {
                        CanonicalId = VisionAgentResourceIdentity.CreateCanonicalId(
                            "camera_binding", "imageacquisition#1", "CameraBindingId"),
                        Status = VisionAgentResourceStatuses.Bound,
                        ResourceKey = "acquire_1.CameraBindingId",
                        ResourceType = "camera_binding",
                        OperatorKey = "imageacquisition#1",
                        OperatorId = "acquire_1",
                        OperatorType = "ImageAcquisition",
                        ParameterName = "CameraBindingId",
                        ValueSummary = "55555555-5555-4555-8555-555555555555",
                        Source = "camera_binding_catalog"
                    }
                ]
            }
        });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.Content.ReadAsStringAsync()).Should().Contain("operation_identity_conflict");
        host.Generation.BuildCallCount.Should().Be(1);
    }

    [Fact(DisplayName = "Existing Project Build validates authoritative revision hash and canonical server flow")]
    public async Task ExistingProjectBuild_ShouldValidateCanonicalBaselineAndIgnoreClientDraft()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var canonicalFlow = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "canonical-server-flow"
        };
        var project = new ProjectDto
        {
            Id = Guid.NewGuid(),
            Name = "authoritative-project",
            PersistenceRevision = 41,
            Flow = canonicalFlow
        };
        host.Projects.Project = project;
        var canonicalHash = ExecutionFlowIdentity.ComputeFlowHash(canonicalFlow.ToEntity());

        using var staleRevision = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            clientOperationId = Guid.NewGuid(),
            target = new
            {
                targetKind = "existing",
                projectId = project.Id,
                persistenceRevision = 40,
                canonicalFlowHash = canonicalHash
            },
            description = "stale project build",
            existingFlowJson = "{\"name\":\"untrusted-client-draft\"}"
        });
        staleRevision.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var staleDocument = JsonDocument.Parse(await staleRevision.Content.ReadAsStringAsync());
        staleDocument.RootElement.GetProperty("errorCode").GetString().Should().Be("project_revision_conflict");
        staleDocument.RootElement.GetProperty("currentBaseline").GetProperty("persistenceRevision").GetInt64()
            .Should().Be(project.PersistenceRevision);

        using var staleHash = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            clientOperationId = Guid.NewGuid(),
            target = new
            {
                targetKind = "existing",
                projectId = project.Id,
                persistenceRevision = project.PersistenceRevision,
                canonicalFlowHash = "sha256:stale"
            },
            description = "stale flow build"
        });
        staleHash.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var staleHashDocument = JsonDocument.Parse(await staleHash.Content.ReadAsStringAsync());
        staleHashDocument.RootElement.GetProperty("errorCode").GetString().Should().Be("canonical_flow_hash_conflict");

        using var accepted = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            clientOperationId = Guid.NewGuid(),
            target = new
            {
                targetKind = "existing",
                projectId = project.Id,
                persistenceRevision = project.PersistenceRevision,
                canonicalFlowHash = canonicalHash
            },
            description = "authoritative project build",
            existingFlowJson = "{\"name\":\"untrusted-client-draft\"}",
            useVisionAgentGenerateFlow = true
        });
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        await host.Generation.WaitForCallAsync();
        host.Projects.GetByIdCallCount.Should().Be(3);
        host.Projects.LastRequestedId.Should().Be(project.Id);
        host.Generation.LastRequest!.ExistingFlowJson.Should().Be(JsonSerializer.Serialize(canonicalFlow));
        host.Generation.LastRequest.ExistingFlowJson.Should().NotContain("untrusted-client-draft");

        using var acceptedDocument = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        var baseline = acceptedDocument.RootElement.GetProperty("operation").GetProperty("projectBaseline");
        baseline.GetProperty("projectId").GetGuid().Should().Be(project.Id);
        baseline.GetProperty("persistenceRevision").GetInt64().Should().Be(project.PersistenceRevision);
        baseline.GetProperty("canonicalFlowHash").GetString().Should().Be(canonicalHash);
    }

    [Fact(DisplayName = "POST AgentRun cancel emits run.cancelled and cancels background request")]
    public async Task Cancel_ShouldEmitCancelledEvent()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await AgentRunEndpointTestHost.CreateAsync(async (request, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return AgentRunEndpointTestHost.SuccessResult();
        });
        var runId = await host.CreateRunAsync("Cancel me");
        await host.Generation.WaitForCallAsync();

        using var cancel = await host.Client.PostAsync($"/api/ai/agent-runs/{runId}/cancel", content: null);

        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Generation.LastCancellationToken.IsCancellationRequested.Should().BeTrue();
        var replay = host.StreamService.Replay(runId)!;
        replay.Events.Last().EventType.Should().Be(AgentRunEventTypes.RunCancelled);
        replay.Summary.Status.Should().Be(AgentRunEventStatuses.Cancelled);
    }

    [Fact(DisplayName = "POST AgentRun cancel missing run returns 404")]
    public async Task CancelMissingRun_ShouldReturnNotFound()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();

        using var response = await host.Client.PostAsync("/api/ai/agent-runs/ar_missing/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "AgentRun failed GenerateFlow emits first fix recommendation")]
    public async Task FailedGeneration_ShouldEmitFirstFixRecommendation()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync((_, _) => Task.FromResult(new AiFlowGenerationResult
        {
            Success = false,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
            FailureType = AiFlowGenerationResult.FailureTypeManualRetryRequired,
            ErrorMessage = "Missing safe metadata.",
            FailureSummary = new AiFailureSummary
            {
                Message = "Missing safe metadata.",
                RepairTarget = "Provide the missing threshold metadata."
            }
        }));
        var runId = await host.CreateRunAsync("fail");

        await host.Generation.WaitForCallAsync();
        await host.WaitForTerminalAsync(runId);

        var replay = host.StreamService.Replay(runId)!;
        replay.Summary.Status.Should().Be(AgentRunEventStatuses.Failed);
        replay.Summary.FirstFixRecommendation.Should().Be("Provide the missing threshold metadata.");
        JsonSerializer.Serialize(replay.Events.Last(), AgentRunEventJson.Options)
            .Should()
            .Contain("firstFixRecommendation");
    }

    private static async Task<string> ReadSseUntilAsync(HttpResponseMessage response, string eventType)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var frame = new StringBuilder();

        while (!cts.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line == null)
            {
                break;
            }

            if (line.Length == 0)
            {
                var text = frame.ToString();
                if (text.Contains($"event: {eventType}", StringComparison.Ordinal))
                {
                    return text;
                }

                frame.Clear();
                continue;
            }

            frame.AppendLine(line);
        }

        throw new TimeoutException($"SSE event '{eventType}' was not received.");
    }

    private static string ResolveOwnerHashForTest(string userId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"agent-run-owner:{userId.Trim()}"));
        return "usr_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static AiProjectTargetRequest NewProjectTarget() => new() { TargetKind = "new" };

    private static VisionAgentPlanModeResult LegacyBlockedAgentRunBuildFromPlanSnapshot()
    {
        var result = new VisionAgentPlanModeResult
        {
            PlanId = "plan-agent-run-entry",
            OriginalUserPrompt = "start build from confirmed plan",
            Goal = "logo surface defect inspection",
            Intent = AiVisionTaskTypes.Unknown,
            Confidence = "low",
            RequirementUnderstanding = ["Legacy snapshot was captured before confirmed answers were applied."],
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = "surface_defect_route",
                Title = "Surface defect route",
                Summary = "Acquisition, defect detection, judgment, and output.",
                Operators = ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
                TemplateDecision = "planner_route"
            },
            ClarificationQuestions = [],
            BlockingReasons =
            [
                "hard_requirement:inspection_object_missing",
                "hard_requirement:task_type_missing",
                "hard_requirement:image_source_missing",
                "hard_requirement:acceptance_criteria_missing"
            ],
            CanBuild = false,
            SemanticExtraction = new VisionAgentSemanticExtractionResult
            {
                IsVisionRequest = true,
                Intent = "new_flow",
                TaskType = AiVisionTaskTypes.Unknown,
                Source = VisionAgentSemanticSources.RuleFallback,
                MetadataOnly = true
            },
            RequirementMaturity = new AiRequirementMaturityResult
            {
                Maturity = AiRequirementMaturity.Ambiguous,
                TaskType = AiVisionTaskTypes.Unknown,
                CanPlan = false,
                CanBuild = false,
                MissingFields = ["inspection_object", "task_type", "image_source", "acceptance_criteria"],
                BlockingReasons = ["inspection_object_missing", "task_type_missing", "image_source_missing", "acceptance_criteria_missing"],
                PublicReason = "Legacy snapshot was not buildable before answers."
            },
            MetadataOnly = true
        };

        return result with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(result)
        };
    }

    private static VisionAgentBuildFromPlanRequest BuildableAgentRunBuildFromPlanRequest()
    {
        return new VisionAgentBuildFromPlanRequest
        {
            PlanId = "plan_buildable",
            PlanHash = "sha256:plan-buildable",
            PlanSnapshot = new VisionAgentPlanModeResult
            {
                PlanId = "plan_buildable",
                PlanHash = "sha256:plan-buildable",
                Goal = "Build from a persisted plan",
                CanBuild = true,
                MetadataOnly = true
            },
            UserSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image_source"] = "camera"
            },
            ConfirmedAnswers =
            [
                new VisionAgentPlanAnswer
                {
                    QuestionId = "image_source",
                    Field = "image_source",
                    Value = "camera",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection
                }
            ],
            BuildIntent = "new",
            OriginalUserPrompt = "Build from a persisted plan",
            MetadataOnly = true
        };
    }

    private static async Task<(string SessionId, string RunId, VisionAgentPublicBuildResultV1 Build, long Revision)>
        CreateG3BuildCandidateAsync(AgentRunEndpointTestHost host)
    {
        using var sessionResponse = await host.Client.PostAsJsonAsync("/api/ai/sessions", new
        {
            clientOperationId = Guid.NewGuid()
        });
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var sessionDocument = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        var sessionId = sessionDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString()!;
        var buildFromPlan = BuildableAgentRunBuildFromPlanRequest() with
        {
            WorkspaceExpectedRevision = 0,
            AnswerRevision = 0,
            ResourceRevision = 0
        };
        using var buildResponse = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            clientOperationId = Guid.NewGuid(),
            target = new { targetKind = "new" },
            description = "Build a candidate for G3 validation",
            sessionId,
            requirementMode = AiRequirementModes.Strict,
            buildFromPlan,
            useVisionAgentGenerateFlow = true,
            agentGenerateFlowMode = AiAgentGenerateFlowModes.Scripted
        });
        buildResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var buildDocument = JsonDocument.Parse(await buildResponse.Content.ReadAsStringAsync());
        var runId = buildDocument.RootElement.GetProperty("runId").GetString()!;
        await host.WaitForTerminalAsync(runId);
        await host.WaitForWorkspaceBuildStatusAsync(sessionId, AgentRunEventStatuses.Completed);
        var snapshot = host.ConversationService.GetSession(sessionId)!.WorkspaceSnapshot!;
        snapshot.PublicBuildResult.Should().NotBeNull();
        return (sessionId, runId, snapshot.PublicBuildResult!, snapshot.Revision);
    }

    private static async Task<(
        string SessionId,
        string PlanRunId,
        string BuildRunId,
        Guid BuildClientOperationId,
        VisionAgentPublicBuildResultV1 Build,
        long Revision)> CreateHandoffReadyCandidateAsync(AgentRunEndpointTestHost host)
    {
        using var sessionResponse = await host.Client.PostAsJsonAsync("/api/ai/sessions", new
        {
            clientOperationId = Guid.NewGuid()
        });
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var sessionDocument = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        var sessionId = sessionDocument.RootElement.GetProperty("session").GetProperty("sessionId").GetString()!;

        using var planResponse = await host.Client.PostAsJsonAsync("/api/ai/agent-plan-runs", new VisionAgentPlanModeRequest
        {
            ClientOperationId = Guid.NewGuid(),
            Description = "Create a handoff-ready inspection flow",
            OriginalUserPrompt = "Create a handoff-ready inspection flow",
            SessionId = sessionId,
            RequirementMode = AiRequirementModes.Strict
        });
        planResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var planDocument = JsonDocument.Parse(await planResponse.Content.ReadAsStringAsync());
        var planRunId = planDocument.RootElement.GetProperty("runId").GetString()!;
        await host.WaitForTerminalAsync(planRunId);
        await WaitForPlanRunBackgroundSettleAsync(host, planRunId, sessionId);

        var planSnapshot = host.ConversationService.GetSession(sessionId)!.WorkspaceSnapshot!;
        var plan = planSnapshot.PendingPlanSnapshot!;
        var buildOperationId = Guid.NewGuid();
        using var buildResponse = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new AgentRunCreateRequest
        {
            ClientOperationId = buildOperationId,
            Target = new AiProjectTargetRequest { TargetKind = "new" },
            Description = "Build the approved handoff candidate",
            SessionId = sessionId,
            RequirementMode = AiRequirementModes.Strict,
            BuildFromPlan = new VisionAgentBuildFromPlanRequest
            {
                PlanId = plan.PlanId,
                PlanHash = plan.PlanHash,
                PlanSnapshot = plan,
                WorkspaceExpectedRevision = planSnapshot.Revision,
                AnswerRevision = planSnapshot.AnswerRevision,
                ResourceRevision = planSnapshot.ResourceRevision,
                BuildIntent = "new",
                OriginalUserPrompt = "Build the approved handoff candidate",
                MetadataOnly = true
            },
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Scripted
        });
        buildResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var buildDocument = JsonDocument.Parse(await buildResponse.Content.ReadAsStringAsync());
        var buildRunId = buildDocument.RootElement.GetProperty("runId").GetString()!;
        await host.WaitForTerminalAsync(buildRunId);
        await host.WaitForWorkspaceBuildStatusAsync(sessionId, AgentRunEventStatuses.Completed);
        var terminalSnapshot = host.ConversationService.GetSession(sessionId)!.WorkspaceSnapshot!;
        terminalSnapshot.PublicBuildResult!.Validation.HandoffEligible.Should().BeTrue();
        using var revalidated = await host.Client.PostAsJsonAsync(
            $"/api/ai/agent-runs/{buildRunId}/revalidate",
            new
            {
                sessionId,
                expectedRevision = terminalSnapshot.Revision,
                clientMutationId = Guid.NewGuid(),
                buildId = terminalSnapshot.PublicBuildResult.BuildId,
                candidateFlowFingerprint = terminalSnapshot.PublicBuildResult.CandidateFlowFingerprint,
                answerRevision = terminalSnapshot.AnswerRevision,
                resourceRevision = terminalSnapshot.ResourceRevision
            });
        revalidated.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = host.ConversationService.GetSession(sessionId)!.WorkspaceSnapshot!;
        snapshot.LifecycleState.Should().Be("build_ready");
        snapshot.PublicBuildResult!.Validation.HandoffEligible.Should().BeTrue();
        return (
            sessionId,
            planRunId,
            buildRunId,
            buildOperationId,
            snapshot.PublicBuildResult,
            snapshot.Revision);
    }

    private static Task<VisionAgentPlanModeResult> HandoffReadyPlanAsync(
        VisionAgentPlanModeRequest request,
        VisionAgentPlanModeResult baseline,
        CancellationToken cancellationToken)
    {
        var plan = baseline with
        {
            PlanId = "plan_handoff_ready",
            Goal = "创建可交接的视觉检测流程",
            CanBuild = true,
            CurrentPhase = VisionAgentPlanPhases.ReadyToBuild,
            BlockingReasons = [],
            ClarificationQuestions = [],
            RemainingPlanFields = [],
            BuildReadiness = new VisionAgentBuildReadinessSnapshot
            {
                CanBuild = true,
                ContractVersion = VisionAgentPlanContractVersions.V2,
                Blockers = [],
                RemainingFields = []
            },
            MetadataOnly = true
        };
        return Task.FromResult(plan with { PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan) });
    }

    private static Task<AiFlowGenerationResult> HandoffReadyBuildResultAsync(
        AiFlowGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = Guid.NewGuid();
        var flow = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "AI 候选流程",
            Operators =
            [
                new OperatorDto
                {
                    Id = operatorId,
                    Name = "阈值判断",
                    Type = OperatorType.Thresholding,
                    X = 160,
                    Y = 120,
                    IsEnabled = true,
                    Parameters =
                    [
                        new ParameterDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "Threshold",
                            DisplayName = "阈值",
                            DataType = "double",
                            Value = 128d,
                            MinValue = 0d,
                            MaxValue = 255d,
                            IsRequired = true
                        }
                    ]
                }
            ],
            Connections = []
        };
        var plan = request.BuildFromPlan?.PlanSnapshot;
        var build = new VisionAgentBuildResult
        {
            BuildId = "build_handoff_ready",
            PlanId = plan?.PlanId ?? string.Empty,
            PlanHash = plan?.PlanHash ?? string.Empty,
            ContractVersion = VisionAgentPlanContractVersions.V2,
            BuildIntent = "new",
            AnswerSetFingerprint = "sha256:" + new string('A', 64),
            Flow = flow,
            WorkflowDraft = flow,
            OperatorPipeline =
            [
                new VisionAgentOperatorPipelineStep
                {
                    TempId = operatorId.ToString("D"),
                    OperatorType = "Thresholding",
                    Source = "rule_fallback",
                    Status = "ready"
                }
            ],
            ValidationPreview = new { isValid = true, blockingIssues = Array.Empty<string>(), warnings = Array.Empty<string>() },
            DryRunResult = new { dryRunSucceeded = true, blockingIssues = Array.Empty<string>(), warnings = Array.Empty<string>() },
            ReadinessReport = new { readyForDeployment = true },
            WorkflowDiff = new VisionAgentWorkflowDiff { AddedNodes = ["Thresholding"] },
            ApplyGate = new VisionAgentApplyGate
            {
                CanvasApplyReady = true,
                RuntimeDraftReady = true,
                DeploymentReady = false,
                Blocked = false,
                Status = "ready_for_handoff",
                FirstFixRecommendation = "候选已具备工作区审核条件。",
                MetadataOnly = true
            },
            MetadataOnly = true
        };
        return Task.FromResult(new AiFlowGenerationResult
        {
            Success = true,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
            GenerationMode = "rule_fallback",
            AiExplanation = "Generated a public canonical handoff candidate.",
            Flow = flow,
            BuildResult = build,
            PendingParameters = [],
            MissingResources = []
        });
    }

    private static List<VisionAgentPlanAnswer> ConfirmedAgentRunBuildFromPlanAnswers()
    {
        return
        [
            AgentRunTextPlanAnswer(VisionAgentPlanAnswerFields.InspectionObject, "logo area"),
            AgentRunTextPlanAnswer(VisionAgentPlanAnswerFields.TaskType, AiVisionTaskTypes.SurfaceDefect),
            AgentRunTextPlanAnswer(VisionAgentPlanAnswerFields.ImageSource, "camera"),
            AgentRunTextPlanAnswer(VisionAgentPlanAnswerFields.AcceptanceCriteria, "scratch is NG")
        ];
    }

    private static VisionAgentPlanAnswer AgentRunTextPlanAnswer(string field, string value)
    {
        return new VisionAgentPlanAnswer
        {
            Field = field,
            Value = value,
            Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
        };
    }

    private static async Task WaitForPlanRunBackgroundSettleAsync(
        AgentRunEndpointTestHost host,
        string runId,
        string sessionId)
    {
        var stableSince = DateTimeOffset.UtcNow;
        var lastEventCount = -1;
        var lastRevision = -1L;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!cts.IsCancellationRequested)
        {
            var replay = host.StreamService.Replay(runId)!;
            var revision = host.ConversationService.GetSession(sessionId)?.WorkspaceSnapshot?.Revision ?? -1L;
            if (replay.Events.Count == lastEventCount && revision == lastRevision)
            {
                if (DateTimeOffset.UtcNow - stableSince >= TimeSpan.FromMilliseconds(200))
                {
                    return;
                }
            }
            else
            {
                lastEventCount = replay.Events.Count;
                lastRevision = revision;
                stableSince = DateTimeOffset.UtcNow;
            }

            await Task.Delay(20, cts.Token);
        }

        throw new TimeoutException("PlanRun background cancellation did not settle.");
    }

    private static void AssertCancelledPlanRunTerminalConsistency(
        AgentRunEndpointTestHost host,
        string runId,
        string sessionId,
        bool expectPrimarySaved)
    {
        var replay = host.StreamService.Replay(runId)!;
        replay.Events.Count(evt => evt.EventType == AgentRunEventTypes.RunCancelled).Should().Be(1);
        replay.Events.Should().NotContain(evt => evt.EventType == AgentRunEventTypes.RunCompleted);
        replay.Events.Should().NotContain(evt => evt.EventType == AgentRunEventTypes.RunFailed);
        replay.Diagnostics.DroppedEventCount.Should().Be(0);
        replay.Summary.Status.Should().Be(AgentRunEventStatuses.Cancelled);

        var session = host.ConversationService.GetSession(sessionId)!;
        var runCancelled = replay.Events.Last(evt => evt.EventType == AgentRunEventTypes.RunCancelled);
        var payload = SerializePayloadElement(runCancelled.Payload);
        var payloadSnapshot = payload.GetProperty("workspaceSnapshot");
        payloadSnapshot.GetProperty("revision").GetInt64().Should().Be(session.WorkspaceSnapshot!.Revision);
        payloadSnapshot.GetProperty("planRunId").GetString().Should().Be(runId);
        payload.GetProperty("persistenceStatus").GetProperty("primaryStoreSaved").GetBoolean().Should().Be(expectPrimarySaved);

        if (expectPrimarySaved)
        {
            payloadSnapshot.GetProperty("planRunStatus").GetString().Should().Be(AgentRunEventStatuses.Cancelled);
            session.WorkspaceSnapshot.PlanRunStatus.Should().Be(AgentRunEventStatuses.Cancelled);
            payload.TryGetProperty("persistenceWarning", out var warning).Should().BeTrue();
            warning.ValueKind.Should().Be(JsonValueKind.Null);
            return;
        }

        payloadSnapshot.GetProperty("planRunStatus").GetString().Should().Be(AgentRunEventStatuses.Running);
        session.WorkspaceSnapshot.PlanRunStatus.Should().Be(AgentRunEventStatuses.Running);
        payload.GetProperty("persistenceWarning").GetProperty("code").GetString()
            .Should()
            .Be("primary_store_save_failed");
    }

    private static JsonElement SerializePayloadElement(object? payload)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, AgentRunEventJson.Options));
        return document.RootElement.Clone();
    }

    private sealed class AgentRunEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _directory;

        private AgentRunEndpointTestHost(
            WebApplication app,
            string directory,
            FakeAiFlowGenerationService generation,
            IAgentRunEventStreamService streamService,
            IConversationalFlowService conversationService,
            IAiOperationReceiptStore operationStore,
            IAiWorkspaceHandoffArtifactStore handoffStore,
            FakeProjectApplicationService projects,
            IVisionAgentBuildTerminalProjector terminalProjector)
        {
            _app = app;
            _directory = directory;
            Generation = generation;
            StreamService = streamService;
            ConversationService = conversationService;
            ConcreteConversationService = (ConversationalFlowService)conversationService;
            OperationStore = operationStore;
            HandoffStore = handoffStore;
            Projects = projects;
            TerminalProjector = terminalProjector;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public string RootDirectory => _directory;

        public FakeAiFlowGenerationService Generation { get; }

        public IAgentRunEventStreamService StreamService { get; }

        public IConversationalFlowService ConversationService { get; }

        public ConversationalFlowService ConcreteConversationService { get; }

        public IAiOperationReceiptStore OperationStore { get; }

        public IAiWorkspaceHandoffArtifactStore HandoffStore { get; }

        public FakeProjectApplicationService Projects { get; }

        public IVisionAgentBuildTerminalProjector TerminalProjector { get; }

        public static async Task<AgentRunEndpointTestHost> CreateAsync(
            Func<AiFlowGenerationRequest, CancellationToken, Task<AiFlowGenerationResult>>? handler = null,
            bool useAuth = false,
            Func<DateTimeOffset>? utcNowProvider = null,
            Func<VisionAgentPlanModeRequest, VisionAgentPlanModeResult, CancellationToken, Task<VisionAgentPlanModeResult>>? planHandler = null,
            Func<VisionAgentIntentRouterRequest, CancellationToken, Task<VisionAgentIntentRouterResult>>? intentRouterHandler = null,
            Func<VisionAgentBuildRevalidationRequest, CancellationToken, Task<VisionAgentBuildRevalidationResult>>? revalidateHandler = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddLogging(logging => logging.ClearProviders());

            var directory = Path.Combine(Path.GetTempPath(), $"cv-agent-run-endpoints-{Guid.NewGuid():N}");
            var redactor = new AgentRunEventRedactor();
            var store = new AgentRunEventStore(directory, redactor);
            var streamService = new AgentRunEventStreamService(store, redactor);
            var conversationService = new ConversationalFlowService(Path.Combine(directory, "sessions"));
            var operationStore = new AiOperationReceiptStore(Path.Combine(directory, "operations"));
            var handoffStore = new AiWorkspaceHandoffArtifactStore(Path.Combine(directory, "handoffs"));
            var projects = new FakeProjectApplicationService();
            var cameraManager = new CameraManager(NullLoggerFactory.Instance);
            cameraManager.LoadBindings(
            [
                new CameraBindingConfig { Id = "camera-binding-01", DisplayName = "Line camera", IsEnabled = true },
                new CameraBindingConfig { Id = "camera-binding-02", DisplayName = "Backup camera", IsEnabled = true },
                new CameraBindingConfig { Id = "camera-binding-disabled", DisplayName = "Disabled camera", IsEnabled = false }
            ], string.Empty);
            if (utcNowProvider != null)
            {
                streamService.UtcNowProvider = utcNowProvider;
            }
            var generation = new FakeAiFlowGenerationService(
                handler ?? ((_, _) => Task.FromResult(SuccessResult())),
                streamService,
                revalidateHandler);

            builder.Services.AddSingleton(redactor);
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton<IVisionAgentBuildProjectionJournal, VisionAgentBuildProjectionJournal>();
            builder.Services.AddSingleton<IConversationalFlowService>(conversationService);
            builder.Services.AddSingleton<IAiOperationReceiptStore>(operationStore);
            builder.Services.AddSingleton<IAiWorkspaceHandoffArtifactStore>(handoffStore);
            builder.Services.AddSingleton<ICameraManager>(cameraManager);
            builder.Services.AddSingleton<IProjectApplicationService>(projects);
            builder.Services.AddSingleton<IAgentRunEventStreamService>(streamService);
            builder.Services.AddSingleton<IAiFlowGenerationService>(generation);
            builder.Services.AddSingleton<IVisionAgentBuildApplicationService>(generation);
            builder.Services.AddSingleton<IVisionAgentBuildTerminalProjector, VisionAgentBuildTerminalProjector>();
            builder.Services.AddScoped<IVisionAgentBuildRunService, VisionAgentBuildRunService>();
            builder.Services.AddSingleton<IVisionAgentToolRegistry, EmptyVisionAgentToolRegistry>();
            builder.Services.AddSingleton<IVisionAgentIntentRouterService>(
                new FakeVisionAgentIntentRouterService(intentRouterHandler));
            builder.Services.AddScoped<IAgentRunEventSink, AgentRunEventSink>();
            if (planHandler != null)
            {
                builder.Services.AddSingleton<IVisionAgentPlanPlannerService>(new FakeVisionAgentPlanPlannerService(planHandler));
            }
            builder.Services.AddScoped<IVisionAgentOrchestrator, VisionAgentOrchestrator>();
            if (useAuth)
            {
                builder.Services.AddSingleton<IAuthService, FakeAuthService>();
            }

            var app = builder.Build();
            if (useAuth)
            {
                app.UseMiddleware<AuthMiddleware>();
            }
            else
            {
                app.Use(async (context, next) =>
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "user-default"),
                        new Claim(ClaimTypes.Name, "default-engineer"),
                        new Claim(ClaimTypes.Role, "Admin")
                    ], "Test"));
                    await next(context);
                });
            }
            app.MapAiSessionEndpoints();
            app.MapAgentRunEndpoints();
            app.MapAiWorkspaceHandoffEndpoints();
            await app.StartAsync();
            var terminalProjector = app.Services.GetRequiredService<IVisionAgentBuildTerminalProjector>();

            return new AgentRunEndpointTestHost(
                app,
                directory,
                generation,
                streamService,
                conversationService,
                operationStore,
                handoffStore,
                projects,
                terminalProjector);
        }

        public static AiFlowGenerationResult SuccessResult()
        {
            return new AiFlowGenerationResult
            {
                Success = true,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                GenerationMode = "agent_run_event_stream",
                AiExplanation = "Generated metadata-only draft.",
                Flow = new OperatorFlowDto(),
                ToolTrace = [new { toolName = "validate_flow", success = true }],
                PendingParameters = [],
                MissingResources = []
            };
        }

        public async Task<string> CreateRunAsync(string description)
        {
            using var response = await Client.PostAsJsonAsync("/api/ai/agent-runs", new
            {
                clientOperationId = Guid.NewGuid(),
                target = new { targetKind = "new" },
                description,
                additionalContext = "Detect scratches on a metal part.",
                useVisionAgentGenerateFlow = true
            });
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("runId").GetString()!;
        }

        public async Task<string> CreatePlanRunAsync(string description, string? sessionId = null)
        {
            using var response = await Client.PostAsJsonAsync("/api/ai/agent-plan-runs", new VisionAgentPlanModeRequest
            {
                ClientOperationId = Guid.NewGuid(),
                Description = description,
                OriginalUserPrompt = description,
                SessionId = sessionId
            });
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("runId").GetString()!;
        }

        public async Task<string> CreateStreamTokenAsync(string runId)
        {
            using var response = await Client.PostAsync($"/api/ai/agent-runs/{runId}/stream-token", content: null);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("streamToken").GetString()!;
        }

        public HttpClient CreateAnonymousClient()
        {
            return _app.GetTestClient();
        }

        public void AuthorizeAs(string token)
        {
            Client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        }

        public async Task WaitForTerminalAsync(string runId)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!cts.IsCancellationRequested)
            {
                var replay = StreamService.Replay(runId);
                if (replay?.Events.Any(evt =>
                        evt.EventType is AgentRunEventTypes.RunCompleted or AgentRunEventTypes.RunFailed or AgentRunEventTypes.RunCancelled) == true)
                {
                    return;
                }

                await Task.Delay(20, cts.Token);
            }

            throw new TimeoutException("AgentRun terminal event was not emitted.");
        }

        public async Task WaitForSessionHistoryCountAsync(string sessionId, int expectedCount)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!cts.IsCancellationRequested)
            {
                var session = ConversationService.GetSession(sessionId);
                if (session?.History.Count >= expectedCount)
                {
                    return;
                }

                await Task.Delay(20, cts.Token);
            }

            throw new TimeoutException($"Conversation session '{sessionId}' did not reach {expectedCount} history entries.");
        }

        public async Task WaitForWorkspaceBuildStatusAsync(string sessionId, string expectedStatus)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!cts.IsCancellationRequested)
            {
                var status = ConversationService.GetSession(sessionId)?.WorkspaceSnapshot?.BuildRunStatus;
                if (string.Equals(status, expectedStatus, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                await Task.Delay(20, cts.Token);
            }

            throw new TimeoutException(
                $"Conversation session '{sessionId}' did not reach Build status '{expectedStatus}'.");
        }

        public async Task WaitForEventAsync(string runId, string eventType)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!cts.IsCancellationRequested)
            {
                var replay = StreamService.Replay(runId);
                if (replay?.Events.Any(evt => evt.EventType == eventType) == true)
                {
                    return;
                }

                await Task.Delay(20, cts.Token);
            }

            throw new TimeoutException($"AgentRun event '{eventType}' was not emitted.");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            if (Directory.Exists(_directory))
            {
                await DeleteDirectoryWithRetryAsync(_directory);
            }
        }

        private static async Task DeleteDirectoryWithRetryAsync(string directory)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }

                    return;
                }
                catch (IOException) when (attempt < 9)
                {
                    await Task.Delay(100);
                }
                catch (UnauthorizedAccessException) when (attempt < 9)
                {
                    await Task.Delay(100);
                }
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class FakeAiFlowGenerationService : IAiFlowGenerationService, IVisionAgentBuildApplicationService
    {
        private readonly Func<AiFlowGenerationRequest, CancellationToken, Task<AiFlowGenerationResult>> _handler;
        private readonly IAgentRunEventStreamService _streamService;
        private readonly Func<VisionAgentBuildRevalidationRequest, CancellationToken, Task<VisionAgentBuildRevalidationResult>>? _revalidateHandler;
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeAiFlowGenerationService(
            Func<AiFlowGenerationRequest, CancellationToken, Task<AiFlowGenerationResult>> handler,
            IAgentRunEventStreamService streamService,
            Func<VisionAgentBuildRevalidationRequest, CancellationToken, Task<VisionAgentBuildRevalidationResult>>? revalidateHandler = null)
        {
            _handler = handler;
            _streamService = streamService;
            _revalidateHandler = revalidateHandler;
        }

        public AiFlowGenerationRequest? LastRequest { get; private set; }

        public BuildCommand? LastCommand { get; private set; }

        public int BuildCallCount { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public async Task<AiFlowGenerationResult> GenerateFlowAsync(
            AiFlowGenerationRequest request,
            Action<string>? onProgress = null,
            Action<AiStreamChunk>? onStreamChunk = null,
            CancellationToken cancellationToken = default,
            Action<GenerateFlowAttachmentReport>? onAttachmentReport = null)
        {
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            _called.TrySetResult();
            return await _handler(request, cancellationToken);
        }

        public async Task<CanonicalBuildOutcome> BuildAsync(
            BuildCommand command,
            CancellationToken cancellationToken)
        {
            BuildCallCount++;
            LastCommand = command;
            LastRequest = command.Request;
            LastCancellationToken = cancellationToken;
            _called.TrySetResult();
            AppendCanonicalStages(command.Request.AgentRunId);
            var result = await _handler(command.Request, cancellationToken);
            NormalizeResult(result, command.Request);
            return new CanonicalBuildOutcome
            {
                Result = result,
                RunId = command.RunId ?? command.Request.AgentRunId ?? string.Empty,
                RequestId = command.RequestId ?? string.Empty,
                Transport = command.Transport,
                CompletionStatus = result.CompletionStatus,
                FailureType = result.FailureType ?? string.Empty,
                FailureCode = result.FailureSummary?.Code ?? string.Empty,
                PlanId = result.PlanId,
                PlanHash = result.PlanHash,
                ContractVersion = result.ContractVersion,
                AnswerSetFingerprint = result.AnswerSetFingerprint,
                RequestedMode = result.RequestedMode,
                EffectiveMode = result.EffectiveMode,
                ToolLoopEntered = result.ToolLoopEntered,
                FallbackReason = result.FallbackReason,
                BuildReadiness = result.BuildReadiness,
                WorkflowDiff = result.BuildResult?.WorkflowDiff,
                ApplyGate = result.BuildResult?.ApplyGate
            };
        }

        public Task<VisionAgentBuildReadinessPreviewResult> PreviewBuildReadinessAsync(
            VisionAgentBuildReadinessPreviewRequest request,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            var readiness = request.PlanSnapshot?.BuildReadiness ?? new VisionAgentBuildReadinessSnapshot
            {
                CanBuild = true,
                ContractVersion = request.PlanSnapshot?.PlanContractVersion ?? VisionAgentPlanContractVersions.V2
            };
            return Task.FromResult(new VisionAgentBuildReadinessPreviewResult
            {
                PlanId = request.PlanId,
                PlanHash = request.PlanHash,
                RequirementMode = request.RequirementMode,
                AnswerRevision = request.AnswerRevision,
                ResourceRevision = request.ResourceRevision,
                AcceptedAnswers = request.ConfirmedAnswers,
                AnswerSetFingerprint = "sha256:test-preview",
                BuildReadiness = readiness,
                PendingConfirmationCount = readiness.RemainingFields.Count,
                ResourcePendingCount = readiness.Blockers.Count(blocker =>
                    blocker.Category == VisionAgentBuildBlockerCategories.ResourcePending),
                HardBlockerCount = readiness.Blockers.Count(blocker =>
                    blocker.BlocksBuild &&
                    blocker.Category != VisionAgentBuildBlockerCategories.ResourcePending),
                MetadataOnly = true
            });
        }

        public async Task<VisionAgentBuildRevalidationResult> RevalidateAsync(
            VisionAgentBuildRevalidationRequest request,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            if (_revalidateHandler != null)
            {
                return await _revalidateHandler(request, cancellationToken);
            }

            var build = request.Build with
            {
                AnswerRevision = request.AnswerRevision,
                ResourceRevision = request.ResourceRevision
            };
            return new VisionAgentBuildRevalidationResult
            {
                Build = build,
                CandidateFlowJson = request.CandidateFlowJson
            };
        }

        public async Task WaitForCallAsync()
        {
            await _called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        private static void NormalizeResult(
            AiFlowGenerationResult result,
            AiFlowGenerationRequest request)
        {
            var build = request.BuildFromPlan;
            var planId = result.PlanId;
            if (string.IsNullOrWhiteSpace(planId))
            {
                planId = result.BuildResult?.PlanId ?? build?.PlanId ?? build?.PlanSnapshot?.PlanId ?? string.Empty;
            }

            var planHash = result.PlanHash;
            if (string.IsNullOrWhiteSpace(planHash))
            {
                planHash = result.BuildResult?.PlanHash ?? build?.PlanHash ?? build?.PlanSnapshot?.PlanHash ?? string.Empty;
            }

            var contractVersion = string.IsNullOrWhiteSpace(result.ContractVersion)
                ? build?.PlanSnapshot?.PlanContractVersion ?? VisionAgentPlanContractVersions.V2
                : result.ContractVersion;
            var requestedMode = AiAgentGenerateFlowModes.Normalize(request.AgentGenerateFlowMode);
            result.PlanId = planId;
            result.PlanHash = planHash;
            result.ContractVersion = contractVersion;
            result.RequestedMode = requestedMode;
            result.EffectiveMode = string.IsNullOrWhiteSpace(result.EffectiveMode)
                ? requestedMode
                : AiAgentGenerateFlowModes.Normalize(result.EffectiveMode);
            result.ToolLoopEntered = result.ToolLoopEntered ||
                                     string.Equals(requestedMode, AiAgentGenerateFlowModes.ToolLoop, StringComparison.OrdinalIgnoreCase);

            result.BuildResult ??= new VisionAgentBuildResult();
            result.BuildResult = result.BuildResult with
            {
                BuildId = string.IsNullOrWhiteSpace(result.BuildResult.BuildId)
                    ? $"build_{request.AgentRunId}"
                    : result.BuildResult.BuildId,
                PlanId = planId,
                PlanHash = planHash,
                ContractVersion = contractVersion,
                RequestedMode = requestedMode,
                EffectiveMode = result.EffectiveMode,
                ToolLoopEntered = result.ToolLoopEntered,
                FallbackReason = result.FallbackReason,
                Flow = result.BuildResult.Flow ?? result.Flow
            };
        }

        private void AppendCanonicalStages(string? runId)
        {
            if (string.IsNullOrWhiteSpace(runId))
            {
                return;
            }

            _streamService.Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.StageCompleted,
                Stage = "canonical_build_contract",
                Title = "Build contract accepted",
                Summary = "BuildFromPlan contract was normalized.",
                Status = AgentRunEventStatuses.Completed,
                Payload = new { metadataOnly = true }
            });
            _streamService.Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.ReadinessChecked,
                Stage = "canonical_build_readiness",
                Title = "Build readiness accepted",
                Summary = "BuildFromPlan readiness was normalized.",
                Status = AgentRunEventStatuses.Completed,
                Payload = new { metadataOnly = true }
            });
        }
    }

    private sealed class FakeVisionAgentPlanPlannerService : IVisionAgentPlanPlannerService
    {
        private readonly Func<VisionAgentPlanModeRequest, VisionAgentPlanModeResult, CancellationToken, Task<VisionAgentPlanModeResult>> _handler;

        public FakeVisionAgentPlanPlannerService(
            Func<VisionAgentPlanModeRequest, VisionAgentPlanModeResult, CancellationToken, Task<VisionAgentPlanModeResult>> handler)
        {
            _handler = handler;
        }

        public Task<VisionAgentPlanModeResult> CreatePlanAsync(
            VisionAgentPlanModeRequest request,
            VisionAgentPlanModeResult ruleBaseline,
            CancellationToken cancellationToken)
        {
            return _handler(request, ruleBaseline, cancellationToken);
        }
    }

    private sealed class FakeVisionAgentIntentRouterService : IVisionAgentIntentRouterService
    {
        private readonly Func<VisionAgentIntentRouterRequest, CancellationToken, Task<VisionAgentIntentRouterResult>> _handler;

        public FakeVisionAgentIntentRouterService(
            Func<VisionAgentIntentRouterRequest, CancellationToken, Task<VisionAgentIntentRouterResult>>? handler)
        {
            _handler = handler ?? ((_, _) => Task.FromResult(new VisionAgentIntentRouterResult
            {
                Intent = "ambiguous_vision_requirement",
                Confidence = "low",
                ShouldOpenPlan = false,
                ShouldBuildDirectly = false,
                CanBuild = false,
                NeedsClarification = true,
                PublicReason = "需求信息不足，暂不可构建。",
                AssistantReply = "请补充检测目标、缺陷、输入来源、OK/NG 规则。",
                RouterSource = "test_router",
                MetadataOnly = true
            }));
        }

        public Task<VisionAgentIntentRouterResult> RouteAsync(
            VisionAgentIntentRouterRequest request,
            CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }

    private sealed class FakeProjectApplicationService : IProjectApplicationService
    {
        public ProjectDto? Project { get; set; }

        public int GetByIdCallCount { get; private set; }

        public Guid? LastRequestedId { get; private set; }

        public Task<ProjectDto?> GetByIdAsync(Guid id)
        {
            GetByIdCallCount++;
            LastRequestedId = id;
            return Task.FromResult(Project?.Id == id ? Project : null);
        }
    }

    private sealed class FakeAuthService : IAuthService
    {
        public Task<AuthResult> LoginAsync(string username, string password)
        {
            return Task.FromResult(AuthResult.Fail("not used"));
        }

        public Task LogoutAsync(string token)
        {
            return Task.CompletedTask;
        }

        public Task<bool> ValidateTokenAsync(string token)
        {
            return Task.FromResult(IsKnownToken(token));
        }

        public Task<ClearVision.Product.Application.Services.UserSession?> GetSessionAsync(string token)
        {
            if (!IsKnownToken(token))
            {
                return Task.FromResult<ClearVision.Product.Application.Services.UserSession?>(null);
            }

            return Task.FromResult<ClearVision.Product.Application.Services.UserSession?>(new()
            {
                UserId = token switch
                {
                    "owner-a-token" => "user-owner-a",
                    "owner-b-token" => "user-owner-b",
                    "engineer-token" => "user-engineer",
                    "operator-token" => "user-operator",
                    _ => "user-default"
                },
                Username = token,
                Role = token switch
                {
                    "engineer-token" => "Engineer",
                    "operator-token" => "Operator",
                    _ => "Admin"
                },
                ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            });
        }

        public Task<AuthResult> ChangePasswordAsync(string userId, string oldPassword, string newPassword)
        {
            return Task.FromResult(AuthResult.Fail("not used"));
        }

        public Task<InitialAdminSetupStatusResponse> GetInitialAdminSetupStatusAsync()
        {
            return Task.FromResult(new InitialAdminSetupStatusResponse());
        }

        public Task<AuthResult> SetupInitialAdminAsync(InitialAdminSetupRequest request)
        {
            return Task.FromResult(AuthResult.Fail("not used"));
        }

        private static bool IsKnownToken(string token)
        {
            return token is "owner-a-token" or "owner-b-token" or "owner-default-token" or
                "engineer-token" or "operator-token";
        }
    }

    private static VisionAgentPlanPublicEvent PlanEvent(
        string stage,
        string status,
        string title,
        string summary,
        Dictionary<string, string>? metadata = null)
    {
        return new VisionAgentPlanPublicEvent
        {
            Stage = stage,
            Status = status,
            Title = title,
            Summary = summary,
            Metadata = metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            MetadataOnly = true
        };
    }
}
