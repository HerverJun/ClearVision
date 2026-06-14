using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Tests.AI.VisionAgentToolLoop;

public sealed class VisionAgentToolLoopBuildTests
{
    [Fact(DisplayName = "BuildFromPlan tool_loop should run VisionAgentLoop and complete through stable BuildOrchestrator")]
    public async Task BuildFromPlanToolLoop_ShouldRunLoopAndStableBuild()
    {
        var sink = new CapturingAgentRunEventSink();
        var tool = new FakeTool("inspect_current_flow", VisionAgentToolPermission.ReadOnly);
        var completion = new ScriptedLoopCompletionSource(
        [
            ToolCall("inspect_current_flow"),
            FinalWorkflowDraft()
        ]);
        var stableBuild = new FakeBuildOrchestrator();
        var orchestrator = CreateOrchestrator(
            sink,
            [tool],
            completion,
            stableBuild);

        var result = await orchestrator.BuildFromPlanAsync(ToolLoopRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        completion.CallCount.Should().Be(2);
        stableBuild.CallCount.Should().Be(1);
        tool.ExecuteCount.Should().Be(1);
        completion.CapturedMessages[1].Should().Contain(message =>
            string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase) &&
            message.Content.Contains("\"tool_result\"", StringComparison.OrdinalIgnoreCase) &&
            message.Content.Contains("inspect_current_flow", StringComparison.OrdinalIgnoreCase));
        sink.Events.Select(evt => evt.EventType).Should().Contain([
            AgentRunEventTypes.ToolLoopStarted,
            AgentRunEventTypes.ToolLoopRoundStarted,
            AgentRunEventTypes.ToolCallRequested,
            AgentRunEventTypes.ToolCallLoopCompleted,
            AgentRunEventTypes.ToolResultAppended,
            AgentRunEventTypes.ToolLoopFinalized,
            AgentRunEventTypes.ToolLoopDraftAccepted
        ]);
        sink.Events.Should().NotContain(evt => evt.EventType == AgentRunEventTypes.ToolLoopFallback);
        result.BuildResult!.ToolEvidenceTimeline.Should().Contain(item =>
            item.Source == "llm_tool_loop" &&
            item.ToolName == "inspect_current_flow");
        result.BuildResult.ToolEvidenceTimeline.Should().Contain(item =>
            item.Source == "fixed_build_orchestrator");
    }

    [Theory(DisplayName = "BuildFromPlan tool_loop should fallback when an LLM requested tool is denied")]
    [InlineData("write_config_draft", VisionAgentToolPermission.ConfigWrite)]
    [InlineData("runtime_package_precheck", VisionAgentToolPermission.DeploymentPrepare)]
    public async Task BuildFromPlanToolLoop_ShouldFallbackOnPermissionDenied(
        string toolName,
        VisionAgentToolPermission permission)
    {
        var sink = new CapturingAgentRunEventSink();
        var deniedTool = new FakeTool(toolName, permission);
        var completion = new ScriptedLoopCompletionSource(
        [
            ToolCall(toolName),
            FinalWorkflowDraft()
        ]);
        var stableBuild = new FakeBuildOrchestrator();
        var orchestrator = CreateOrchestrator(
            sink,
            [deniedTool],
            completion,
            stableBuild);

        var result = await orchestrator.BuildFromPlanAsync(ToolLoopRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        deniedTool.ExecuteCount.Should().Be(0);
        stableBuild.CallCount.Should().Be(1);
        sink.Events.Should().Contain(evt => evt.EventType == AgentRunEventTypes.ToolCallDenied);
        sink.Events.Should().Contain(evt =>
            evt.EventType == AgentRunEventTypes.ToolLoopFallback &&
            Json(evt.Payload).GetProperty("fallbackReason").GetString() == "tool_permission_denied");
        result.BuildResult!.ToolEvidenceTimeline.Should().Contain(item =>
            item.Source == "llm_tool_loop" &&
            item.Status == AgentRunEventStatuses.Blocked &&
            item.WarningCode == "tool_permission_denied");
        result.BuildResult.ToolEvidenceTimeline.Should().Contain(item =>
            item.Source == "fallback_build_orchestrator" &&
            item.ToolName == "stable_build_tool");
    }

    [Fact(DisplayName = "BuildFromPlan tool_loop should fallback when an LLM requests an unknown tool")]
    public async Task BuildFromPlanToolLoop_ShouldFallbackOnUnknownTool()
    {
        var sink = new CapturingAgentRunEventSink();
        var completion = new ScriptedLoopCompletionSource(
        [
            ToolCall("unknown_experimental_tool"),
            FinalWorkflowDraft()
        ]);
        var stableBuild = new FakeBuildOrchestrator();
        var orchestrator = CreateOrchestrator(
            sink,
            [],
            completion,
            stableBuild);

        var result = await orchestrator.BuildFromPlanAsync(ToolLoopRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        stableBuild.CallCount.Should().Be(1);
        sink.Events.Should().Contain(evt => evt.EventType == AgentRunEventTypes.ToolCallDenied);
        sink.Events.Should().Contain(evt =>
            evt.EventType == AgentRunEventTypes.ToolLoopFallback &&
            Json(evt.Payload).GetProperty("fallbackReason").GetString() == "unknown_tool");
        result.BuildResult!.ToolEvidenceTimeline.Should().Contain(item =>
            item.Source == "llm_tool_loop" &&
            item.ToolName == "unknown_experimental_tool" &&
            item.Status == AgentRunEventStatuses.Blocked &&
            item.WarningCode == "unknown_tool");
        result.BuildResult.ToolEvidenceTimeline.Should().Contain(item =>
            item.Source == "fallback_build_orchestrator" &&
            item.ToolName == "stable_build_tool");
    }

    [Fact(DisplayName = "BuildFromPlan tool_loop should fallback when MaxToolRounds is exceeded")]
    public async Task BuildFromPlanToolLoop_ShouldFallbackOnMaxRounds()
    {
        var sink = new CapturingAgentRunEventSink();
        var tool = new FakeTool("inspect_current_flow", VisionAgentToolPermission.ReadOnly);
        var completion = new ScriptedLoopCompletionSource(
        [
            ToolCall("inspect_current_flow"),
            ToolCall("inspect_current_flow")
        ]);
        var stableBuild = new FakeBuildOrchestrator();
        var orchestrator = CreateOrchestrator(
            sink,
            [tool],
            completion,
            stableBuild,
            new VisionAgentLoopOptions
            {
                MaxToolRounds = 1
            });

        var result = await orchestrator.BuildFromPlanAsync(ToolLoopRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        tool.ExecuteCount.Should().Be(1);
        sink.Events.Should().Contain(evt => evt.EventType == AgentRunEventTypes.ToolLoopFailed);
        sink.Events.Should().Contain(evt =>
            evt.EventType == AgentRunEventTypes.ToolLoopFallback &&
            Json(evt.Payload).GetProperty("fallbackReason").GetString() == "failed_with_tool_limit");
        result.BuildResult!.ToolEvidenceTimeline.Should().Contain(item =>
            item.ToolName == "tool_loop_fallback" &&
            item.WarningCode == "failed_with_tool_limit");
    }

    [Fact(DisplayName = "BuildFromPlan tool_loop should fallback when draft validation rejects final")]
    public async Task BuildFromPlanToolLoop_ShouldRejectDraftAndFallback()
    {
        var sink = new CapturingAgentRunEventSink();
        var validateFlow = new FakeTool(
            "validate_flow",
            VisionAgentToolPermission.Simulation,
            (_, _) => VisionAgentToolResult.Ok(new
            {
                valid = false,
                blockingIssues = new[] { new { code = "invalid_flow", message = "Invalid draft." } },
                warnings = Array.Empty<object>(),
                missingResources = Array.Empty<object>(),
                pendingParameters = Array.Empty<object>(),
                metadataOnly = true
            }));
        var completion = new ScriptedLoopCompletionSource([FinalWorkflowDraft()]);
        var stableBuild = new FakeBuildOrchestrator();
        var orchestrator = CreateOrchestrator(
            sink,
            [validateFlow],
            completion,
            stableBuild);

        var result = await orchestrator.BuildFromPlanAsync(ToolLoopRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        stableBuild.CallCount.Should().Be(1);
        sink.Events.Should().Contain(evt =>
            evt.EventType == AgentRunEventTypes.ToolLoopDraftRejected &&
            Json(evt.Payload).GetProperty("rejectionReason").GetString() == "validate_flow_failed");
        sink.Events.Should().Contain(evt =>
            evt.EventType == AgentRunEventTypes.ToolLoopFallback &&
            Json(evt.Payload).GetProperty("fallbackReason").GetString() == "validate_flow_failed");
        sink.Events.Should().NotContain(evt => evt.EventType == AgentRunEventTypes.ToolLoopDraftAccepted);
    }

    [Fact(DisplayName = "BuildFromPlan tool_loop should fallback when duplicate tool calls exceed threshold")]
    public async Task BuildFromPlanToolLoop_ShouldFallbackOnDuplicateToolCall()
    {
        var sink = new CapturingAgentRunEventSink();
        var tool = new FakeTool("inspect_current_flow", VisionAgentToolPermission.ReadOnly);
        var completion = new ScriptedLoopCompletionSource(
        [
            ToolCall("inspect_current_flow"),
            ToolCall("inspect_current_flow")
        ]);
        var stableBuild = new FakeBuildOrchestrator();
        var orchestrator = CreateOrchestrator(
            sink,
            [tool],
            completion,
            stableBuild,
            new VisionAgentLoopOptions
            {
                MaxToolRounds = 4,
                MaxRepeatedToolCalls = 1
            });

        var result = await orchestrator.BuildFromPlanAsync(ToolLoopRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        tool.ExecuteCount.Should().Be(1);
        stableBuild.CallCount.Should().Be(1);
        sink.Events.Should().Contain(evt =>
            evt.EventType == AgentRunEventTypes.ToolLoopFailed &&
            Json(evt.Payload).GetProperty("failureType").GetString() == "duplicate_tool_call");
        sink.Events.Should().Contain(evt =>
            evt.EventType == AgentRunEventTypes.ToolLoopFallback &&
            Json(evt.Payload).GetProperty("fallbackReason").GetString() == "duplicate_tool_call");
    }

    [Fact(DisplayName = "BuildFromPlan tool_loop should reject invalid final JSON and fallback")]
    public async Task BuildFromPlanToolLoop_ShouldFallbackOnInvalidFinalJson()
    {
        var sink = new CapturingAgentRunEventSink();
        var completion = new ScriptedLoopCompletionSource(
        [
            "not json",
            """{"kind":"final","draftEdits":"invalid"}"""
        ]);
        var stableBuild = new FakeBuildOrchestrator();
        var orchestrator = CreateOrchestrator(
            sink,
            [new FakeTool("inspect_current_flow", VisionAgentToolPermission.ReadOnly)],
            completion,
            stableBuild,
            new VisionAgentLoopOptions
            {
                MaxInvalidJsonResponses = 2
            });

        var result = await orchestrator.BuildFromPlanAsync(ToolLoopRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        stableBuild.CallCount.Should().Be(1);
        sink.Events.Should().Contain(evt =>
            evt.EventType == AgentRunEventTypes.ToolLoopFailed &&
            Json(evt.Payload).GetProperty("failureType").GetString() == "invalid_json");
        sink.Events.Should().Contain(evt =>
            evt.EventType == AgentRunEventTypes.ToolLoopDraftRejected &&
            Json(evt.Payload).GetProperty("rejectionReason").GetString() == "invalid_json");
        sink.Events.Should().Contain(evt =>
            evt.EventType == AgentRunEventTypes.ToolLoopFallback &&
            Json(evt.Payload).GetProperty("fallbackReason").GetString() == "invalid_json");
    }

    [Fact(DisplayName = "BuildFromPlan tool_loop cancellation should stop before fallback")]
    public async Task BuildFromPlanToolLoop_ShouldHonorCancellation()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new BlockingLoopCompletionSource(gate.Task);
        var stableBuild = new FakeBuildOrchestrator();
        var orchestrator = CreateOrchestrator(
            new CapturingAgentRunEventSink(),
            [new FakeTool("inspect_current_flow", VisionAgentToolPermission.ReadOnly)],
            completion,
            stableBuild);
        using var cts = new CancellationTokenSource();
        var task = orchestrator.BuildFromPlanAsync(ToolLoopRequest(), cts.Token);

        await completion.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await FluentActions.Awaiting(() => task)
            .Should()
            .ThrowAsync<OperationCanceledException>();
        stableBuild.CallCount.Should().Be(0);
    }

    [Fact(DisplayName = "BuildFromPlan tool_loop public events and payload should redact sensitive metadata")]
    public async Task BuildFromPlanToolLoop_ShouldNotLeakSensitiveMetadata()
    {
        var sink = new CapturingAgentRunEventSink();
        var completion = new ScriptedLoopCompletionSource([FinalWorkflowDraft()]);
        var syntheticImageDataUri = "data:image/png;" + "base64," + new string('A', 96);
        var orchestrator = CreateOrchestrator(
            sink,
            [new FakeTool("inspect_current_flow", VisionAgentToolPermission.ReadOnly)],
            completion,
            new FakeBuildOrchestrator());

        var result = await orchestrator.BuildFromPlanAsync(ToolLoopRequest(
            @"inspect C:\factory\secret.png token=abc123 sk-secret DB1.DBX0.0 192.168.1.20 " + syntheticImageDataUri),
            CancellationToken.None);

        var publicJson = JsonSerializer.Serialize(new { result.BuildResult, sink.Events }, AgentRunEventJson.Options);
        publicJson.Should().NotContain("rawPrompt");
        publicJson.Should().NotContain("systemPrompt");
        publicJson.Should().NotContain("chainOfThought");
        publicJson.Should().NotContain("reasoning_content");
        publicJson.Should().NotContain(@"C:\factory");
        publicJson.Should().NotContain("sk-secret");
        publicJson.Should().NotContain("DB1.DBX0.0");
        publicJson.Should().NotContain("192.168.1.20");
        publicJson.Should().NotContain("data:image/png;base64");
    }

    private static VisionAgentOrchestrator CreateOrchestrator(
        CapturingAgentRunEventSink sink,
        IReadOnlyList<FakeTool> tools,
        IVisionAgentLoopCompletionSource completionSource,
        IVisionAgentBuildOrchestrator buildOrchestrator,
        VisionAgentLoopOptions? loopOptions = null)
    {
        var options = loopOptions ?? new VisionAgentLoopOptions
        {
            MaxToolRounds = 4,
            MaxToolCallsPerRound = 4,
            MaxToolResultChars = 64_000
        };
        var allTools = DefaultDraftValidationTools()
            .Concat(tools)
            .GroupBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        var registry = new VisionAgentToolRegistryAdapter(allTools);
        var loop = new VisionAgentLoop(
            registry,
            new VisionAgentProtocolParser(),
            new AgentPromptBuilder(),
            Options.Create(options),
            sink);
        return new VisionAgentOrchestrator(
            registry,
            new FakeGenerationService(),
            sink,
            buildOrchestrator,
            toolLoop: loop,
            toolLoopCompletionSource: completionSource,
            loopOptions: Options.Create(options),
            redactor: new AgentRunEventRedactor());
    }

    private static AiFlowGenerationRequest ToolLoopRequest(string description = "metal surface scratch detection")
    {
        var plan = new VisionAgentPlanModeResult
        {
            PlanId = "plan_tool_loop_test",
            OriginalUserPrompt = description,
            Goal = description,
            Intent = "surface_defect",
            Confidence = "high",
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = "surface_defect",
                Operators = ["ImageAcquisition", "SurfaceDefectDetection", "ResultOutput"],
                TemplateDecision = "catalog_match"
            },
            MetadataOnly = true
        };

        return new AiFlowGenerationRequest(description, Mode: GenerateFlowMode.New)
        {
            AgentRunId = "ar_tool_loop_test",
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.ToolLoop,
            BuildFromPlan = new VisionAgentBuildFromPlanRequest
            {
                PlanId = plan.PlanId,
                PlanSnapshot = plan,
                BuildIntent = "new",
                OriginalUserPrompt = description,
                MetadataOnly = true
            }
        };
    }

    private static IReadOnlyList<FakeTool> DefaultDraftValidationTools()
    {
        return
        [
            new FakeTool(
                "validate_flow",
                VisionAgentToolPermission.Simulation,
                (_, _) => VisionAgentToolResult.Ok(new
                {
                    valid = true,
                    blockingIssues = Array.Empty<object>(),
                    warnings = Array.Empty<object>(),
                    missingResources = Array.Empty<object>(),
                    pendingParameters = Array.Empty<object>(),
                    metadataOnly = true
                })),
            new FakeTool(
                "dryrun_flow",
                VisionAgentToolPermission.Simulation,
                (_, _) => VisionAgentToolResult.Ok(new
                {
                    dryRunSucceeded = true,
                    warnings = Array.Empty<object>(),
                    blockingIssues = Array.Empty<object>(),
                    missingResources = Array.Empty<object>(),
                    metadataOnly = true
                })),
            new FakeTool(
                "runtime_package_precheck",
                VisionAgentToolPermission.DeploymentPrepare,
                (_, _) => VisionAgentToolResult.Ok(new
                {
                    readyForDeployment = false,
                    missingResources = Array.Empty<object>(),
                    blockingIssues = Array.Empty<object>(),
                    pendingActions = Array.Empty<object>(),
                    metadataOnly = true
                }))
        ];
    }

    private static string ToolCall(string name)
    {
        return JsonSerializer.Serialize(new
        {
            kind = "tool_call",
            toolCalls = new[]
            {
                new
                {
                    id = "call_1",
                    name,
                    arguments = new { value = "metadata-only" }
                }
            }
        });
    }

    private static string FinalWorkflowDraft()
    {
        return JsonSerializer.Serialize(new
        {
            kind = "final",
            workflowDraft = new
            {
                operators = new[]
                {
                    new
                    {
                        tempId = "op_acq",
                        operatorType = "ImageAcquisition",
                        parameters = new { CameraId = "<pending-camera-binding>" }
                    }
                },
                connections = Array.Empty<object>(),
                metadataOnly = true
            },
            missingResources = Array.Empty<object>(),
            pendingParameters = Array.Empty<object>()
        });
    }

    private static JsonElement Json(object? value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, AgentRunEventJson.Options));
        return doc.RootElement.Clone();
    }

    private sealed class ScriptedLoopCompletionSource : IVisionAgentLoopCompletionSource
    {
        private readonly Queue<string> _responses;

        public ScriptedLoopCompletionSource(IEnumerable<string> responses)
        {
            _responses = new Queue<string>(responses);
        }

        public int CallCount { get; private set; }
        public List<IReadOnlyList<VisionAgentLoopMessage>> CapturedMessages { get; } = [];

        public Task<string> CompleteAsync(
            VisionAgentLoopCompletionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            CapturedMessages.Add(request.Messages.ToList());
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : FinalWorkflowDraft());
        }
    }

    private sealed class BlockingLoopCompletionSource : IVisionAgentLoopCompletionSource
    {
        private readonly Task _gate;

        public BlockingLoopCompletionSource(Task gate)
        {
            _gate = gate;
        }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> CompleteAsync(
            VisionAgentLoopCompletionRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _gate.WaitAsync(cancellationToken);
            return FinalWorkflowDraft();
        }
    }

    private sealed class FakeBuildOrchestrator : IVisionAgentBuildOrchestrator
    {
        public int CallCount { get; private set; }

        public Task<AiFlowGenerationResult> BuildAsync(
            AiFlowGenerationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                Flow = new OperatorFlowDto(),
                GenerationMode = "stable_build",
                BuildResult = new VisionAgentBuildResult
                {
                    BuildId = "build_tool_loop_test",
                    ToolEvidenceTimeline =
                    [
                        new VisionAgentToolEvidence
                        {
                            Stage = "workflow_draft",
                            ToolName = "stable_build_tool",
                            Source = "fixed_build_orchestrator",
                            InputSummary = "Stable build input.",
                            OutputSummary = "Stable build completed.",
                            Status = AgentRunEventStatuses.Completed,
                            EvidenceId = "ev_stable",
                            MetadataOnly = true,
                            RedactionPass = true
                        }
                    ],
                    WorkflowDraft = new { operators = Array.Empty<object>(), metadataOnly = true },
                    ApplyGate = new VisionAgentApplyGate
                    {
                        CanvasApplyReady = true,
                        RuntimeDraftReady = true,
                        DeploymentReady = false,
                        Blocked = false
                    },
                    MetadataOnly = true
                }
            });
        }
    }

    private sealed class FakeGenerationService : IAiFlowGenerationService
    {
        public Task<AiFlowGenerationResult> GenerateFlowAsync(
            AiFlowGenerationRequest request,
            Action<string>? onProgress = null,
            Action<AiStreamChunk>? onStreamChunk = null,
            CancellationToken cancellationToken = default,
            Action<GenerateFlowAttachmentReport>? onAttachmentReport = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                Flow = new OperatorFlowDto()
            });
        }
    }

    private sealed class VisionAgentToolRegistryAdapter : IVisionAgentToolRegistry
    {
        private readonly IReadOnlyDictionary<string, FakeTool> _tools;

        public VisionAgentToolRegistryAdapter(IReadOnlyList<FakeTool> tools)
        {
            _tools = tools.ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<VisionAgentToolDescriptor> ListTools()
        {
            return _tools.Values.Select(VisionAgentToolDescriptor.FromTool).ToList();
        }

        public bool TryGet(string name, out IVisionAgentTool tool)
        {
            if (_tools.TryGetValue(name, out var found))
            {
                tool = found;
                return true;
            }

            tool = null!;
            return false;
        }

        public Task<VisionAgentToolResult> ExecuteAsync(
            string name,
            VisionAgentToolContext context,
            JsonElement arguments,
            CancellationToken cancellationToken)
        {
            return _tools.TryGetValue(name, out var tool)
                ? tool.ExecuteAsync(context, arguments, cancellationToken)
                : Task.FromResult(VisionAgentToolResult.Fail("unknown_tool", $"Unknown fake tool '{name}'."));
        }
    }

    private sealed class FakeTool : IVisionAgentTool
    {
        private readonly Func<VisionAgentToolContext, JsonElement, VisionAgentToolResult>? _execute;

        public FakeTool(
            string name,
            VisionAgentToolPermission permission,
            Func<VisionAgentToolContext, JsonElement, VisionAgentToolResult>? execute = null)
        {
            Name = name;
            Permission = permission;
            _execute = execute;
        }

        public string Name { get; }
        public string DisplayName => Name;
        public string Description => "Fake tool_loop test tool.";
        public string Category => "test";
        public VisionAgentToolPermission Permission { get; }
        public JsonElement ParametersSchema { get; } = Schema();
        public int ExecuteCount { get; private set; }

        public Task<VisionAgentToolResult> ExecuteAsync(
            VisionAgentToolContext context,
            JsonElement arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteCount++;
            return Task.FromResult(_execute?.Invoke(context, arguments) ?? VisionAgentToolResult.Ok(new
            {
                tool = Name,
                observed = true,
                metadataOnly = true
            }));
        }
    }

    private sealed class CapturingAgentRunEventSink : IAgentRunEventSink
    {
        public List<AgentRunEventDraft> Events { get; } = [];

        public void Append(string? runId, AgentRunEventDraft draft)
        {
            Events.Add(draft);
        }

        public void StageStarted(string? runId, string stage, string title, string summary, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.StageStarted,
                Stage = stage,
                Title = title,
                Summary = summary,
                Status = AgentRunEventStatuses.Running,
                Payload = payload
            });
        }

        public void StageCompleted(string? runId, string stage, string title, string summary, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.StageCompleted,
                Stage = stage,
                Title = title,
                Summary = summary,
                Status = AgentRunEventStatuses.Completed,
                Payload = payload
            });
        }

        public void ToolStarted(string? runId, string stage, string toolName, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.ToolCallStarted,
                Stage = stage,
                Title = toolName,
                Status = AgentRunEventStatuses.Running,
                Payload = payload
            });
        }

        public void ToolCompleted(string? runId, string stage, string toolName, long durationMs, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.ToolCallCompleted,
                Stage = stage,
                Title = toolName,
                Status = AgentRunEventStatuses.Completed,
                Payload = payload
            });
        }

        public void ToolFailed(string? runId, string stage, string toolName, long durationMs, string summary, object? payload = null)
        {
            Append(runId, new AgentRunEventDraft
            {
                EventType = AgentRunEventTypes.ToolCallFailed,
                Stage = stage,
                Title = toolName,
                Summary = summary,
                Status = AgentRunEventStatuses.Failed,
                Payload = payload
            });
        }
    }

    private static JsonElement Schema()
    {
        using var doc = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return doc.RootElement.Clone();
    }
}
