using System.Net;
using System.Net.Http.Json;
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
        var scratch = scratchDoc.RootElement;
        var wire = wireDoc.RootElement;

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
            .Contain("defect_definition")
            .And.NotContain("sequence_rule");
        scratch.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();

        wire.GetProperty("intent").GetString().Should().Be("wire_sequence");
        wire.GetProperty("clarificationQuestions").EnumerateArray()
            .Select(question => question.GetProperty("id").GetString())
            .Should()
            .Contain("sequence_rule")
            .And.NotContain("defect_definition");
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
    }

    [Fact(DisplayName = "Plan run replay carries redacted PlanResult without private reasoning or raw prompts")]
    public async Task CreatePlanRunReplay_ShouldRedactPrivatePlanPayload()
    {
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
                    "station 192.168.10.45 DB1.DBW0 data:image/png;base64,AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
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
        await host.Generation.WaitForCallAsync();

        var request = host.Generation.LastRequest!;
        request.AgentRunId.Should().StartWith("ar_");
        request.Description.Should().Be("Modify current flow");
        request.AdditionalContext.Should().Be("keep thresholds");
        request.SessionId.Should().Be("session-1");
        request.ExistingFlowJson.Should().Be("{\"operators\":[]}");
        request.Attachments.Should().BeEmpty();
        request.Mode.Should().Be(GenerateFlowMode.Modify);
        request.RuntimePreviewConsent.Should().BeFalse();
        request.UseVisionAgentGenerateFlow.Should().BeTrue();
        request.TemplateSelection.Should().NotBeNull();
        request.TemplateSelection!.Mode.Should().Be("template_fill");
    }

    [Fact(DisplayName = "POST AgentRun preserves structured BuildFromPlan input and replays Plan Build payload")]
    public async Task CreateRun_ShouldPreserveBuildFromPlanContractAndReplayEvents()
    {
        await using var host = await AgentRunEndpointTestHost.CreateAsync();
        const string currentFlowSnapshot = "{\"operators\":[{\"id\":\"existing-camera\"}],\"connections\":[]}";

        using var response = await host.Client.PostAsJsonAsync("/api/ai/agent-runs", new
        {
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

        var replay = host.StreamService.Replay(runId)!;
        replay.Events.Select(evt => evt.Stage).Should().ContainInOrder([
            "plan_generation",
            "assumption_confirmation",
            "requirement_parsing"
        ]);
        var completed = replay.Events.Single(evt => evt.EventType == AgentRunEventTypes.RunCompleted);
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

            replay.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            cancel.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            token.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            host.AuthorizeAs("owner-a-token");
            gate.TrySetResult();
        }
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
        wrongRun.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var authorized = await host.CreateAnonymousClient().GetAsync($"/api/ai/agent-runs/{runId}/events?streamToken={Uri.EscapeDataString(streamToken)}", HttpCompletionOption.ResponseHeadersRead);
        authorized.StatusCode.Should().Be(HttpStatusCode.OK);

        using var reused = await host.CreateAnonymousClient().GetAsync($"/api/ai/agent-runs/{runId}/events?streamToken={Uri.EscapeDataString(streamToken)}", HttpCompletionOption.ResponseHeadersRead);
        reused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var expiringToken = await host.CreateStreamTokenAsync(runId);
        now = now.AddSeconds(61);
        using var expired = await host.CreateAnonymousClient().GetAsync($"/api/ai/agent-runs/{runId}/events?streamToken={Uri.EscapeDataString(expiringToken)}", HttpCompletionOption.ResponseHeadersRead);
        expired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

    private sealed class AgentRunEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _directory;

        private AgentRunEndpointTestHost(
            WebApplication app,
            string directory,
            FakeAiFlowGenerationService generation,
            IAgentRunEventStreamService streamService)
        {
            _app = app;
            _directory = directory;
            Generation = generation;
            StreamService = streamService;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public FakeAiFlowGenerationService Generation { get; }

        public IAgentRunEventStreamService StreamService { get; }

        public static async Task<AgentRunEndpointTestHost> CreateAsync(
            Func<AiFlowGenerationRequest, CancellationToken, Task<AiFlowGenerationResult>>? handler = null,
            bool useAuth = false,
            Func<DateTimeOffset>? utcNowProvider = null,
            Func<VisionAgentPlanModeRequest, VisionAgentPlanModeResult, CancellationToken, Task<VisionAgentPlanModeResult>>? planHandler = null)
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
            if (utcNowProvider != null)
            {
                streamService.UtcNowProvider = utcNowProvider;
            }
            var generation = new FakeAiFlowGenerationService(handler ?? ((_, _) => Task.FromResult(SuccessResult())));

            builder.Services.AddSingleton(redactor);
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton<IAgentRunEventStreamService>(streamService);
            builder.Services.AddSingleton<IAiFlowGenerationService>(generation);
            builder.Services.AddSingleton<IVisionAgentToolRegistry, EmptyVisionAgentToolRegistry>();
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
            app.MapAgentRunEndpoints();
            await app.StartAsync();

            return new AgentRunEndpointTestHost(app, directory, generation, streamService);
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
                description,
                useVisionAgentGenerateFlow = true
            });
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("runId").GetString()!;
        }

        public async Task<string> CreatePlanRunAsync(string description)
        {
            using var response = await Client.PostAsJsonAsync("/api/ai/agent-plan-runs", new
            {
                description,
                originalUserPrompt = description
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
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    private sealed class FakeAiFlowGenerationService : IAiFlowGenerationService
    {
        private readonly Func<AiFlowGenerationRequest, CancellationToken, Task<AiFlowGenerationResult>> _handler;
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeAiFlowGenerationService(Func<AiFlowGenerationRequest, CancellationToken, Task<AiFlowGenerationResult>> handler)
        {
            _handler = handler;
        }

        public AiFlowGenerationRequest? LastRequest { get; private set; }

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

        public async Task WaitForCallAsync()
        {
            await _called.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
                    _ => "user-default"
                },
                Username = token,
                Role = "Admin",
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
            return token is "owner-a-token" or "owner-b-token" or "owner-default-token";
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
