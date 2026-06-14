using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Tests.AI.VisionAgentPlannerCompletion;

public sealed class VisionAgentPlannerCompletionTests
{
    [Fact(DisplayName = "Default DI registers LLM planner completion but keeps Agent GenerateFlow disabled")]
    public void DefaultDi_ShouldRegisterLlmCompletionAndKeepAgentDisabled()
    {
        var services = new ServiceCollection();
        services.AddAiFlowGeneration(new ConfigurationBuilder().Build());

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IVisionAgentPlannerCompletionSource) &&
            descriptor.ImplementationType == typeof(LlmVisionAgentPlannerCompletionSource));
        new AgentGenerateFlowOptions().Enabled.Should().BeFalse();
    }

    [Fact(DisplayName = "Planner completion source should be controlled by options")]
    public async Task CompletionSource_ShouldBeControlledByOptions()
    {
        var connector = new FakeConnector(FinalWorkflowDraft(WireSequenceFlow()));
        var source = CreateLlmCompletionSource(
            connector,
            new AgentPlannerCompletionOptions { Enabled = false });

        var act = async () => await source.CompleteAsync(CompletionRequest("wire sequence"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*disabled by options*");
        connector.CallCount.Should().Be(0);
    }

    [Fact(DisplayName = "Developer planner request should trigger LLM planner completion source")]
    public async Task DeveloperPlannerRequest_ShouldTriggerLlmCompletionSource()
    {
        var connector = new FakeConnector(FinalWorkflowDraft(TemplateMatchingFlow()));
        var service = CreatePlannerGenerateFlowService(connector);

        var result = await service.GenerateFlowAsync(
            PlannerRequest("template matching alignment"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.GenerationMode.Should().Be("agent_planner");
        connector.CallCount.Should().Be(1);
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.TemplateMatching);
    }

    [Fact(DisplayName = "Developer scripted request should not trigger LLM planner completion source")]
    public async Task DeveloperScriptedRequest_ShouldNotTriggerLlmCompletionSource()
    {
        var connector = new FakeConnector(FinalWorkflowDraft(TemplateMatchingFlow()));
        var service = CreatePlannerGenerateFlowService(connector);

        var result = await service.GenerateFlowAsync(
            new AiFlowGenerationRequest("template matching alignment")
            {
                UseVisionAgentGenerateFlow = true,
                AgentGenerateFlowMode = AiAgentGenerateFlowModes.Scripted
            },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.GenerationMode.Should().Be("agent_controlled_scripted");
        connector.CallCount.Should().Be(0);
    }

    [Fact(DisplayName = "LLM tool_call should pass policy and execute allowed tool")]
    public async Task CompletionToolCall_ShouldPassPolicyAndExecuteAllowedTool()
    {
        var connector = new FakeConnector(
            ToolCall("list_operator_catalog", new { keyword = "wire" }),
            FinalWorkflowDraft(WireSequenceFlow()));
        var service = CreatePlannerGenerateFlowService(connector);

        var result = await service.GenerateFlowAsync(
            PlannerRequest("terminal wire sequence inspection"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        Trace(result).Select(item => item.GetProperty("toolName").GetString())
            .Should()
            .Contain("list_operator_catalog");
        connector.CallCount.Should().Be(2);
    }

    [Fact(DisplayName = "Illegal planner tool should be rejected by policy")]
    public async Task IllegalTool_ShouldBeRejectedByPolicy()
    {
        var connector = new FakeConnector(ToolCall("delete_workflow", new { id = "flow" }));
        var service = CreatePlannerGenerateFlowService(connector);

        var result = await service.GenerateFlowAsync(
            PlannerRequest("delete workflow"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("tool policy denied");
        result.ErrorMessage.Should().Contain("outside the allowed tool set");
    }

    [Fact(DisplayName = "Preview planner tool should be rejected by policy by default")]
    public void PreviewTool_ShouldBeRejectedByPolicyByDefault()
    {
        var previewToolName = "capture_" + "test_frame";
        var policy = new AgentToolCallPolicy();

        var result = policy.ValidateToolName(previewToolName);

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be("runtime_preview_consent_required");
    }

    [Fact(DisplayName = "Invalid planner JSON should repair once successfully")]
    public async Task InvalidJsonRepair_ShouldSucceedOnce()
    {
        var connector = new FakeConnector("not json", FinalWorkflowDraft(WireSequenceFlow()));
        var source = CreateLlmCompletionSource(connector);

        var completion = await source.CompleteAsync(
            CompletionRequest("wire sequence"),
            CancellationToken.None);

        completion.Should().Contain("workflowDraft");
        connector.CallCount.Should().Be(2);
        connector.Requests[1].SystemPrompt.Should().Contain("Repair");
    }

    [Fact(DisplayName = "Invalid planner JSON repair failure should return controlled failure")]
    public async Task InvalidJsonRepairFailure_ShouldReturnControlledFailure()
    {
        var connector = new FakeConnector("not json", "still not json");
        var service = CreatePlannerGenerateFlowService(connector);

        var result = await service.GenerateFlowAsync(
            PlannerRequest("wire sequence"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("completion repair failed");
        connector.CallCount.Should().Be(2);
    }

    [Fact(DisplayName = "Planner final workflowDraft should generate flow")]
    public async Task PlannerFinalWorkflowDraft_ShouldGenerateFlow()
    {
        var connector = new FakeConnector(FinalWorkflowDraft(HoleDistanceFlow()));
        var service = CreatePlannerGenerateFlowService(connector);

        var result = await service.GenerateFlowAsync(
            PlannerRequest("hole distance measurement"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var flow = Flow(result);
        flow.Operators.Select(op => op.Type).Should().Contain(OperatorType.CircleMeasurement);
        flow.Operators.Select(op => op.Type).Should().Contain(OperatorType.Measurement);
    }

    [Fact(DisplayName = "Planner final draftEdits should edit existingFlowJson")]
    public async Task PlannerFinalDraftEdits_ShouldEditExistingFlow()
    {
        var existing = JsonSerializer.Serialize(TemplateMatchingWithoutOutputFlow());
        var connector = new FakeConnector(FinalDraftEdits(new object[]
        {
            new
            {
                op = "add_operator",
                @operator = Operator("op_out", "ResultOutput", new Dictionary<string, string>
                {
                    ["Channel"] = "<pending-output-channel>"
                })
            },
            new
            {
                op = "add_connection",
                connection = Connection("op_judge", "Result", "op_out", "Input")
            }
        }));
        var service = CreatePlannerGenerateFlowService(connector);

        var result = await service.GenerateFlowAsync(
            PlannerRequest("add output channel", existing),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.ResultOutput);
    }

    [Fact(DisplayName = "Planner failure should fall back to scripted mode")]
    public async Task PlannerFailure_ShouldFallbackToScriptedMode()
    {
        var connector = new FakeConnector("not json", "still not json");
        var service = CreatePlannerGenerateFlowService(
            connector,
            agentOptions: new AgentGenerateFlowOptions
            {
                Mode = AiAgentGenerateFlowModes.Planner,
                FallbackToScriptedOnPlannerFailure = true
            });

        var result = await service.GenerateFlowAsync(
            PlannerRequest("template matching alignment"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.GenerationMode.Should().Be("agent_planner_scripted_fallback");
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.TemplateMatching);
    }

    [Fact(DisplayName = "Missing resources should not block workflow draft")]
    public async Task MissingResources_ShouldNotBlockWorkflowDraft()
    {
        var connector = new FakeConnector(PrecheckChain(WireSequenceFlow()));
        var service = CreatePlannerGenerateFlowService(connector);

        var result = await service.GenerateFlowAsync(
            PlannerRequest("terminal wire sequence inspection"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Flow.Should().NotBeNull();
        var precheck = ValidationPreview(result).GetProperty("deploymentPrecheck");
        precheck.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        precheck.GetProperty("readyForDeployment").GetBoolean().Should().BeFalse();
        result.MissingResources.Select(resource => resource.ResourceType).Should().Contain("model_resource");
    }

    [Fact(DisplayName = "GenerateFlow response should map pending actions missing resources validation preview and tool trace")]
    public async Task ResponseMapping_ShouldExposeAgentFields()
    {
        var connector = new FakeConnector(PrecheckChain(TemplateMatchingFlow()));
        var service = CreatePlannerGenerateFlowService(connector);

        var result = await service.GenerateFlowAsync(
            PlannerRequest("template matching alignment"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.MissingResources.Select(resource => resource.ResourceType).Should().Contain("template_artifact");
        Json(result.PendingActions).GetRawText().Should().Contain("Template");
        ValidationPreview(result).GetProperty("structuralValidation").ValueKind.Should().Be(JsonValueKind.Object);
        ValidationPreview(result).GetProperty("dryRun").ValueKind.Should().Be(JsonValueKind.Object);
        ValidationPreview(result).GetProperty("deploymentPrecheck").ValueKind.Should().Be(JsonValueKind.Object);
        Trace(result).Select(item => item.GetProperty("permission").GetString())
            .Should()
            .Contain(nameof(VisionAgentToolPermission.DeploymentPrepare));
    }

    [Fact(DisplayName = "Prompt composer should include allowed tools request messages and summaries")]
    public void PromptComposer_ShouldIncludePlannerContext()
    {
        var composer = new AgentPlannerPromptComposer();
        var request = CompletionRequest(
            "replace template path",
            existingFlowJson: JsonSerializer.Serialize(TemplateMatchingWithoutOutputFlow())) with
        {
            Messages =
            [
                new VisionAgentLoopMessage("assistant", ToolCall("validate_flow", new { flow = TemplateMatchingFlow() })),
                new VisionAgentLoopMessage("user", "{\"kind\":\"tool_result\",\"toolResults\":[{\"name\":\"validate_flow\",\"data\":{\"blockingIssues\":[]}}]}")
            ],
            FlowDraft = Element(TemplateMatchingFlow()),
            ValidationSummary = Element(new { blockingIssues = Array.Empty<object>() }),
            DryRunSummary = Element(new { dryRunSucceeded = true }),
            DeploymentPrecheck = Element(new { workflowDraftAllowed = true, readyForDeployment = false })
        };

        var prompt = composer.Compose(request, new AgentPlannerCompletionOptions { MaxSummaryChars = 200 });
        var promptText = prompt.SystemPrompt + Environment.NewLine +
                         string.Join(Environment.NewLine, prompt.Messages.Select(message => message.Content));

        promptText.Should().Contain("list_operator_catalog");
        promptText.Should().Contain("replace template path");
        promptText.Should().Contain("existingFlowJsonSummary");
        promptText.Should().Contain("flowDraftSummary");
        promptText.Should().Contain("validationSummary");
        promptText.Should().Contain("dryRunSummary");
        promptText.Should().Contain("deploymentPrecheckSummary");
        promptText.Should().Contain("complete ordered tool plan");
        promptText.Should().Contain("Plan the complete ordered tool sequence or return final draft");
        promptText.Should().NotContain("Plan the next " + "tool call");
        promptText.Should().Contain("match_flow_template -> get_flow_template_skeleton -> validate_flow -> dryrun_flow");
        promptText.Should().Contain("get_operator_schema -> validate_flow -> runtime_package_precheck");
        promptText.Should().Contain("RuntimePreview consent=true");
        promptText.Should().Contain("RuntimePreview consent=false");
        promptText.Should().Contain("ConfigWrite denied");
        promptText.Should().Contain("DeploymentPrepare non-precheck denied");
    }

    [Fact(DisplayName = "Planner completion source guard should avoid runtime preview hardware network and process APIs")]
    public void SourceGuard_ShouldAvoidDisallowedApis()
    {
        var agentSource = ReadSourceUnder(Path.Combine(GetProductRoot(), "src", "ClearVision.Product.Infrastructure", "AI", "Agent"));
        var forbidden = new[]
        {
            "CameraTestFrameTool",
            "ReplayFlowWithFrameTool",
            "AcquireSingleFrameAsync",
            "EnumerateCamerasAsync",
            "GetOrCreateByBindingAsync",
            "HttpClient",
            "TcpClient",
            "Socket",
            "File.ReadAllBytes",
            "Cv2.ImRead",
            "Image.FromFile",
            "Process.Start",
            "ProcessStartInfo",
            "cmd.exe",
            "powershell.exe",
            "execute_command"
        };

        forbidden.Should().OnlyContain(fragment =>
            !agentSource.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static LlmVisionAgentPlannerCompletionSource CreateLlmCompletionSource(
        FakeConnector connector,
        AgentPlannerCompletionOptions? options = null)
    {
        return new LlmVisionAgentPlannerCompletionSource(
            new AiGenerationOrchestrator(
                new FakeModelSelector(),
                new FakeConnectorFactory(connector)),
            new AgentPlannerPromptComposer(),
            new JsonToolCallRepair(),
            Options.Create(options ?? new AgentPlannerCompletionOptions()));
    }

    private static VisionAgentGenerateFlowService CreatePlannerGenerateFlowService(
        FakeConnector connector,
        AgentGenerateFlowOptions? agentOptions = null,
        VisionAgentLoopOptions? loopOptions = null,
        AgentPlannerCompletionOptions? completionOptions = null)
    {
        loopOptions ??= new VisionAgentLoopOptions
        {
            MaxToolRounds = 8,
            MaxToolCallsPerRound = 4,
            MaxToolResultChars = 64_000
        };
        loopOptions.Normalize();
        agentOptions ??= new AgentGenerateFlowOptions
        {
            Mode = AiAgentGenerateFlowModes.Planner,
            FallbackToScriptedOnPlannerFailure = false
        };
        var parser = new VisionAgentProtocolParser();
        var planner = new VisionAgentPlannerService(
            CreateLlmCompletionSource(connector, completionOptions),
            parser,
            new AgentToolCallPolicy(),
            new AgentPlannerPromptBuilder());

        return new VisionAgentGenerateFlowService(
            CreateLoop(loopOptions),
            Options.Create(loopOptions),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentGenerateFlowService>>(),
            Options.Create(agentOptions),
            planner,
            parser,
            new AgentWorkflowDraftEditor());
    }

    private static VisionAgentLoop CreateLoop(VisionAgentLoopOptions options)
    {
        return new VisionAgentLoop(
            CreateRegistry(),
            new VisionAgentProtocolParser(),
            new AgentPromptBuilder(),
            Options.Create(options));
    }

    private static VisionAgentToolRegistry CreateRegistry()
    {
        return new VisionAgentToolRegistry(
        [
            new OperatorCatalogTool(),
            new OperatorSchemaTool(),
            new OperatorKnowledgeTool(),
            new FlowTemplateMatchTool(),
            new FlowTemplateSkeletonTool(),
            new CurrentFlowInspectTool(),
            new FlowValidationTool(),
            new DryRunFlowTool(),
            new RuntimePackagePrecheckTool()
        ]);
    }

    private static AgentPlannerCompletionRequest CompletionRequest(
        string description,
        string? existingFlowJson = null)
    {
        var policy = new AgentToolCallPolicy();
        return new AgentPlannerCompletionRequest
        {
            GenerationRequest = PlannerRequest(description, existingFlowJson),
            PlannerPrompt = new AgentPlannerPromptBuilder().Build(
                PlannerRequest(description, existingFlowJson),
                policy.ListAllowedToolNames()),
            AllowedToolNames = policy.ListAllowedToolNames()
        };
    }

    private static AiFlowGenerationRequest PlannerRequest(
        string description,
        string? existingFlowJson = null)
    {
        return new AiFlowGenerationRequest(description, ExistingFlowJson: existingFlowJson)
        {
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Planner
        };
    }

    private static string[] PrecheckChain(object flow)
    {
        return
        [
            ToolCall("validate_flow", new { flow }),
            ToolCall("dryrun_flow", new { flow }),
            ToolCall("runtime_package_precheck", new
            {
                flow,
                dryRunSummary = new { dryRunSucceeded = true }
            }),
            FinalWorkflowDraft(flow)
        ];
    }

    private static object WireSequenceFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "<pending-camera-binding>" }),
                Operator("op_roi", "RoiManager"),
                Operator("op_detect", "DeepLearning", new Dictionary<string, string> { ["ModelPath"] = "<pending-model-path>" }),
                Operator("op_judge", "ResultJudgment"),
                Operator("op_out", "ResultOutput", new Dictionary<string, string> { ["Channel"] = "<pending-output-channel>" })
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_roi", "Image"),
                Connection("op_roi", "RoiImage", "op_detect", "Image"),
                Connection("op_detect", "Detections", "op_judge", "Input"),
                Connection("op_judge", "Result", "op_out", "Input")
            }
        };
    }

    private static object TemplateMatchingFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "<pending-camera-binding>" }),
                Operator("op_match", "TemplateMatching", new Dictionary<string, string> { ["TemplatePath"] = "<pending-template-path>" }),
                Operator("op_judge", "ResultJudgment"),
                Operator("op_out", "ResultOutput", new Dictionary<string, string> { ["Channel"] = "<pending-output-channel>" })
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_match", "Image"),
                Connection("op_match", "Score", "op_judge", "Input"),
                Connection("op_judge", "Result", "op_out", "Input")
            }
        };
    }

    private static object HoleDistanceFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "<pending-camera-binding>" }),
                Operator("op_circle_a", "CircleMeasurement"),
                Operator("op_circle_b", "CircleMeasurement"),
                Operator("op_distance", "MeasureDistance", new Dictionary<string, string> { ["Unit"] = "mm", ["Tolerance"] = "<pending-tolerance>" }),
                Operator("op_judge", "ResultJudgment"),
                Operator("op_out", "ResultOutput", new Dictionary<string, string> { ["Channel"] = "<pending-output-channel>" })
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_circle_a", "Image"),
                Connection("op_cam", "Image", "op_circle_b", "Image"),
                Connection("op_circle_a", "Center", "op_distance", "PointA"),
                Connection("op_circle_b", "Center", "op_distance", "PointB"),
                Connection("op_distance", "Distance", "op_judge", "Input"),
                Connection("op_judge", "Result", "op_out", "Input")
            }
        };
    }

    private static object TemplateMatchingWithoutOutputFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "<pending-camera-binding>" }),
                Operator("op_match", "TemplateMatching", new Dictionary<string, string> { ["TemplatePath"] = "template://old" }),
                Operator("op_judge", "ResultJudgment")
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_match", "Image"),
                Connection("op_match", "Score", "op_judge", "Input")
            }
        };
    }

    private static object Operator(
        string tempId,
        string operatorType,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        return new
        {
            tempId,
            operatorType,
            displayName = operatorType,
            parameters = parameters ?? new Dictionary<string, string>()
        };
    }

    private static object Connection(
        string sourceTempId,
        string sourcePortName,
        string targetTempId,
        string targetPortName)
    {
        return new
        {
            sourceTempId,
            sourcePortName,
            targetTempId,
            targetPortName
        };
    }

    private static string ToolCall(string name, object arguments)
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
                    arguments
                }
            }
        });
    }

    private static string FinalWorkflowDraft(object flow)
    {
        return JsonSerializer.Serialize(new
        {
            kind = "final",
            workflowDraft = flow
        });
    }

    private static string FinalDraftEdits(object edits)
    {
        return JsonSerializer.Serialize(new
        {
            kind = "final",
            draftEdits = edits
        });
    }

    private static OperatorFlowDto Flow(AiFlowGenerationResult result)
    {
        result.Flow.Should().BeOfType<OperatorFlowDto>();
        return (OperatorFlowDto)result.Flow!;
    }

    private static IReadOnlyList<JsonElement> Trace(AiFlowGenerationResult result)
    {
        return Json(result.ToolTrace)
            .EnumerateArray()
            .Select(item => item.Clone())
            .ToList();
    }

    private static JsonElement ValidationPreview(AiFlowGenerationResult result)
    {
        return Json(result.ValidationPreview);
    }

    private static JsonElement Json(object? value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return doc.RootElement.Clone();
    }

    private static JsonElement Element(object value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return doc.RootElement.Clone();
    }

    private static string ReadSourceUnder(string directory)
    {
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));
    }

    private static string GetProductRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    }

    private sealed class FakeConnector : IAiConnector
    {
        private readonly Queue<string> _responses;

        public FakeConnector(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public int CallCount { get; private set; }
        public List<FakeCompletionRequest> Requests { get; } = new();

        public Task<AiCompletionResult> CompleteAsync(
            string systemPrompt,
            List<ChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new FakeCompletionRequest(systemPrompt, messages.ToList()));
            CallCount++;
            var content = _responses.Count > 0
                ? _responses.Dequeue()
                : FinalWorkflowDraft(WireSequenceFlow());
            return Task.FromResult(new AiCompletionResult { Content = content });
        }

        public Task<AiCompletionResult> StreamCompleteAsync(
            string systemPrompt,
            List<ChatMessage> messages,
            Action<AiStreamChunk> onChunk,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Planner completion tests use non-streaming completion only.");
        }
    }

    private sealed record FakeCompletionRequest(
        string SystemPrompt,
        List<ChatMessage> Messages);

    private sealed class FakeConnectorFactory : IAiConnectorFactory
    {
        private readonly IAiConnector _connector;

        public FakeConnectorFactory(IAiConnector connector)
        {
            _connector = connector;
        }

        public IAiConnector CreateConnector(AiModelConfig modelConfig) => _connector;
    }

    private sealed class FakeModelSelector : IAiModelSelector
    {
        private readonly AiModelConfig _model = new()
        {
            Id = "planner-test-model",
            Name = "Planner Test Model",
            Model = "planner-test-model",
            Provider = "OpenAI Compatible",
            IsActive = true,
            RoleBindings = ["generation"]
        };

        public AiModelConfig SelectGenerationModel() => _model;

        public AiModelConfig SelectModelForRole(string role) => _model;

        public (AiModelConfig Model, string Reason) SelectModelForRoleWithReason(string role) =>
            (_model, "test");
    }
}
