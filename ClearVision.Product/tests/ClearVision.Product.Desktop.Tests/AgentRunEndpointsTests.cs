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
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Middleware;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClearVision.Product.Desktop.Tests;

public sealed class AgentRunEndpointsTests
{
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

    [Fact(DisplayName = "POST Agent plan readiness preview returns canonical readiness without creating AgentRun")]
    public async Task PreviewPlanReadiness_ShouldNotCreateRunOrProjectSession()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        var plan = LegacyBlockedAgentRunBuildFromPlanSnapshot();

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-plan/readiness-preview", new
        {
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
        host.ConversationService.GetSession("agent-ui-contract").Should().BeNull();
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
                PlanWarnings =
                [
                    "rawPrompt=hidden prompt",
                    "systemPrompt=hidden system",
                    "reasoning_content: private trace",
                    @"model path C:\factory\models\secret.onnx",
                    $"station 192.168.10.45 DB1.DBW0 {syntheticImageDataUri}"
                ],
                PublicEvents =
                [
                    PlanEvent("planning_with_model", "completed", "模型规划完成", "模型已返回公开结构化规划候选。")
                ],
                MetadataOnly = true
            };
            return Task.FromResult(plan with
            {
                PlanHash = VisionAgentOrchestrator.ComputePlanHash(plan)
            });
        });

        var runId = await host.CreatePlanRunAsync(@"inspect C:\factory\secret.png token=abc123");
        await host.WaitForTerminalAsync(runId);

        using var response = await host.Client.GetAsync($"/api/ai/agent-runs/{runId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayJson = await response.Content.ReadAsStringAsync();

        replayJson.Should().Contain("\"planResult\"");
        replayJson.Should().NotContain("rawPrompt");
        replayJson.Should().NotContain("systemPrompt");
        replayJson.Should().NotContain("chainOfThought");
        replayJson.Should().NotContain("reasoning_content");
        replayJson.Should().NotContain(@"C:\factory");
        replayJson.Should().NotContain("192.168.10.45");
        replayJson.Should().NotContain("DB1.DBW0");
        replayJson.Should().NotContain("data:image/png;base64");
        replayJson.Should().NotContain("abc123");
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

    [Fact(DisplayName = "AgentRun create preserves tool_loop GenerateFlow mode")]
    public async Task CreateRun_ShouldPreserveToolLoopMode()
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

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await host.Generation.WaitForCallAsync();

        host.Generation.LastRequest!.AgentGenerateFlowMode.Should().Be(AiAgentGenerateFlowModes.ToolLoop);
        await host.WaitForTerminalAsync(host.Generation.LastRequest.AgentRunId!);
        var replay = host.StreamService.Replay(host.Generation.LastRequest.AgentRunId!)!;
        replay.Events.Last().EventType.Should().Be(AgentRunEventTypes.RunCompleted);
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

    [Fact(DisplayName = "AI endpoints allow Admin and Engineer but reject Operator and unauthenticated users")]
    public async Task AiEndpoints_ShouldEnforceRoleAndAuthenticationMatrix()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync(useAuth: true);

        host.AuthorizeAs("owner-a-token");
        using var admin = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId = Guid.NewGuid() });
        admin.StatusCode.Should().Be(HttpStatusCode.Created);

        host.AuthorizeAs("engineer-token");
        using var engineer = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId = Guid.NewGuid() });
        engineer.StatusCode.Should().Be(HttpStatusCode.Created);

        host.AuthorizeAs("operator-token");
        using var operatorSession = await host.Client.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId = Guid.NewGuid() });
        using var operatorBuild = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
            clientOperationId = Guid.NewGuid(),
            target = new { targetKind = "new" },
            description = "operator must not build"
        });
        operatorSession.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        operatorBuild.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var anonymous = host.CreateAnonymousClient();
        using var unauthenticated = await anonymous.PostAsJsonAsync("/api/ai/sessions", new { clientOperationId = Guid.NewGuid() });
        unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
            "buildRunId", "buildRunStatus", "buildClientOperationId", "projectBaseline", "requirementMode",
            "planQuestionSelections", "confirmedPlanAnswers", "optimisticPlanAnswers", "answerRevision",
            "readinessPreview", "planAcceptedRecommendedDefaults", "planTerminalSequence", "updatedAtUtc"
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
            FakeProjectApplicationService projects,
            IVisionAgentBuildTerminalProjector terminalProjector)
        {
            _app = app;
            _directory = directory;
            Generation = generation;
            StreamService = streamService;
            ConversationService = conversationService;
            ConcreteConversationService = (ConversationalFlowService)conversationService;
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

        public FakeProjectApplicationService Projects { get; }

        public IVisionAgentBuildTerminalProjector TerminalProjector { get; }

        public static async Task<AgentRunEndpointTestHost> CreateAsync(
            Func<AiFlowGenerationRequest, CancellationToken, Task<AiFlowGenerationResult>>? handler = null,
            bool useAuth = false,
            Func<DateTimeOffset>? utcNowProvider = null,
            Func<VisionAgentPlanModeRequest, VisionAgentPlanModeResult, CancellationToken, Task<VisionAgentPlanModeResult>>? planHandler = null,
            Func<VisionAgentIntentRouterRequest, CancellationToken, Task<VisionAgentIntentRouterResult>>? intentRouterHandler = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddLogging();

            var directory = Path.Combine(Path.GetTempPath(), $"cv-agent-run-endpoints-{Guid.NewGuid():N}");
            var redactor = new AgentRunEventRedactor();
            var store = new AgentRunEventStore(directory, redactor);
            var streamService = new AgentRunEventStreamService(store, redactor);
            var conversationService = new ConversationalFlowService(Path.Combine(directory, "sessions"));
            var operationStore = new AiOperationReceiptStore(Path.Combine(directory, "operations"));
            var projects = new FakeProjectApplicationService();
            if (utcNowProvider != null)
            {
                streamService.UtcNowProvider = utcNowProvider;
            }
            var generation = new FakeAiFlowGenerationService(
                handler ?? ((_, _) => Task.FromResult(SuccessResult())),
                streamService);

            builder.Services.AddSingleton(redactor);
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton<IVisionAgentBuildProjectionJournal, VisionAgentBuildProjectionJournal>();
            builder.Services.AddSingleton<IConversationalFlowService>(conversationService);
            builder.Services.AddSingleton<IAiOperationReceiptStore>(operationStore);
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
            await app.StartAsync();
            var terminalProjector = app.Services.GetRequiredService<IVisionAgentBuildTerminalProjector>();

            return new AgentRunEndpointTestHost(
                app,
                directory,
                generation,
                streamService,
                conversationService,
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
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeAiFlowGenerationService(
            Func<AiFlowGenerationRequest, CancellationToken, Task<AiFlowGenerationResult>> handler,
            IAgentRunEventStreamService streamService)
        {
            _handler = handler;
            _streamService = streamService;
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
