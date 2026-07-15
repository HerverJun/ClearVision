using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.DryRun;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Tests.AI.VisionAgentPlanner;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class VisionAgentPlannerTests
{
    [Fact(DisplayName = "Default GenerateFlow should not trigger Agent planner")]
    public async Task DefaultGenerateFlow_ShouldNotTriggerAgentPlanner()
    {
        var agent = new FakeAgentGenerateFlowService(_ => throw new InvalidOperationException("agent should not run"));
        var service = CreateAiFlowGenerationService(agent, new AgentGenerateFlowOptions
        {
            Enabled = true,
            Mode = AiAgentGenerateFlowModes.Planner
        });

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest("hello"));

        result.Success.Should().BeTrue();
        agent.CallCount.Should().Be(0);
    }

    [Fact(DisplayName = "Planner mode should fall back to scripted mode when no completion source is configured")]
    public async Task PlannerMode_ShouldFallbackToScriptedMode()
    {
        var service = CreatePlannerService(
            new NoOpVisionAgentPlannerCompletionSource(),
            new AgentGenerateFlowOptions
            {
                Mode = AiAgentGenerateFlowModes.Planner,
                FallbackToScriptedOnPlannerFailure = true
            });

        var result = await service.GenerateFlowAsync(PlannerRequest("template matching alignment"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.GenerationMode.Should().Be("agent_planner_scripted_fallback");
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.TemplateMatching);
    }

    [Fact(DisplayName = "Planner mode should generate wire sequence workflow")]
    public async Task PlannerMode_ShouldGenerateWireSequenceWorkflow()
    {
        var result = await CreatePlannerService(NewTemplatePlanner("wire_sequence_inspection"))
            .GenerateFlowAsync(PlannerRequest("terminal wire sequence inspection"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.GenerationMode.Should().Be("agent_planner");
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.DeepLearning);
        Trace(result).Select(item => item.GetProperty("toolName").GetString())
            .Should()
            .Contain(["list_operator_catalog", "get_flow_template_skeleton", "validate_flow", "dryrun_flow", "runtime_package_precheck"]);
    }

    [Fact(DisplayName = "Planner mode should generate template matching workflow")]
    public async Task PlannerMode_ShouldGenerateTemplateMatchingWorkflow()
    {
        var result = await CreatePlannerService(NewTemplatePlanner("template_matching_alignment"))
            .GenerateFlowAsync(PlannerRequest("template matching alignment"), CancellationToken.None);

        result.Success.Should().BeTrue();
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.TemplateMatching);
        result.MissingResources.Select(item => item.ResourceType).Should().Contain("template_artifact");
    }

    [Fact(DisplayName = "Planner mode should generate hole distance measurement workflow")]
    public async Task PlannerMode_ShouldGenerateHoleDistanceWorkflow()
    {
        var result = await CreatePlannerService(NewTemplatePlanner("hole_distance_measurement"))
            .GenerateFlowAsync(PlannerRequest("hole distance measurement in mm"), CancellationToken.None);

        result.Success.Should().BeTrue();
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.CircleMeasurement);
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.Measurement);
    }

    [Fact(DisplayName = "Planner mode should edit existing flow by adding ResultOutput")]
    public async Task PlannerMode_ShouldEditExistingFlowByAddingResultOutput()
    {
        var edited = AddResultOutputFlow();
        var result = await CreatePlannerService(EditPlanner(edited))
            .GenerateFlowAsync(
                PlannerRequest("add output channel", JsonSerializer.Serialize(TemplateMatchingWithoutOutputFlow())),
                CancellationToken.None);

        result.Success.Should().BeTrue();
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.ResultOutput);
        Flow(result).Connections.Should().HaveCountGreaterThan(1);
    }

    [Fact(DisplayName = "Planner mode should edit TemplateMatching placeholder parameters")]
    public async Task PlannerMode_ShouldEditTemplateMatchingPlaceholder()
    {
        var edited = ReplaceTemplatePathFlow("<pending-template-path>");
        var result = await CreatePlannerService(EditPlanner(edited))
            .GenerateFlowAsync(
                PlannerRequest("replace template path with placeholder", JsonSerializer.Serialize(TemplateMatchingWithoutOutputFlow())),
                CancellationToken.None);

        result.Success.Should().BeTrue();
        Json(result.PendingActions).GetRawText().Should().Contain("Template");
        Flow(result).Operators.Single(op => op.Type == OperatorType.TemplateMatching)
            .Parameters.Single(parameter => parameter.Name == "TemplatePath")
            .Value
            .Should()
            .Be("<pending-template-path>");
    }

    [Fact(DisplayName = "Planner missing resources should allow workflow draft")]
    public async Task PlannerMode_MissingResources_ShouldAllowWorkflowDraft()
    {
        var result = await CreatePlannerService(NewTemplatePlanner("template_matching_alignment"))
            .GenerateFlowAsync(PlannerRequest("template matching alignment"), CancellationToken.None);

        var precheck = ValidationPreview(result).GetProperty("deploymentPrecheck");
        precheck.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        precheck.GetProperty("readyForDeployment").GetBoolean().Should().BeFalse();
        result.MissingResources.Should().NotBeEmpty();
        result.PendingActions.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "Planner structural errors should enter validationPreview blockingIssues")]
    public async Task PlannerMode_StructuralError_ShouldEnterValidationPreview()
    {
        var result = await CreatePlannerService(EditPlanner(BrokenConnectionFlow()))
            .GenerateFlowAsync(
                PlannerRequest("validate broken edit", JsonSerializer.Serialize(TemplateMatchingWithoutOutputFlow())),
                CancellationToken.None);

        result.Success.Should().BeTrue();
        Codes(ValidationPreview(result).GetProperty("structuralValidation"), "blockingIssues")
            .Should()
            .Contain("invalid_connection");
    }

    [Fact(DisplayName = "Planner policy should reject non-whitelisted tools")]
    public async Task PlannerPolicy_ShouldRejectNonWhitelistedTools()
    {
        var service = CreatePlannerService(
            new DelegatePlannerCompletionSource((_, _) => ToolCall("delete_workflow", new { })),
            new AgentGenerateFlowOptions
            {
                Mode = AiAgentGenerateFlowModes.Planner,
                FallbackToScriptedOnPlannerFailure = false
            });

        var result = await service.GenerateFlowAsync(PlannerRequest("delete workflow"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("tool policy denied");
        result.ErrorMessage.Should().Contain("outside the allowed tool set");
    }

    [Fact(DisplayName = "Planner policy should reject RuntimePreview tool names by default")]
    public void PlannerPolicy_ShouldRejectRuntimePreviewToolNamesByDefault()
    {
        var policy = new AgentToolCallPolicy();
        var runtimePreviewToolName = "capture_" + "test_frame";

        var result = policy.ValidateToolName(runtimePreviewToolName);

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be("runtime_preview_consent_required");
    }

    [Fact(DisplayName = "Planner should surface RuntimePreview consent pendingAction when not authorized")]
    public async Task Planner_ShouldSurfaceRuntimePreviewPendingActionWhenNotAuthorized()
    {
        var runtimePreviewToolName = "capture_" + "test_frame";
        var service = CreatePlannerService(
            new DelegatePlannerCompletionSource((request, index) => index switch
            {
                0 => ToolCall(runtimePreviewToolName, new { cameraBindingId = "mock-cam" }),
                _ => JsonSerializer.Serialize(new
                {
                    kind = "final",
                    workflowDraft = TemplateMatchingWithoutOutputFlow()
                })
            }),
            new AgentGenerateFlowOptions
            {
                Mode = AiAgentGenerateFlowModes.Planner,
                FallbackToScriptedOnPlannerFailure = false
            });

        var result = await service.GenerateFlowAsync(PlannerRequest("preview frame"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PendingActions.Should().Contain(action =>
            Json(action).GetRawText().Contains("AuthorizeRuntimePreview", StringComparison.OrdinalIgnoreCase));
        Trace(result).Should().Contain(trace =>
            trace.GetProperty("toolName").GetString() == runtimePreviewToolName &&
            trace.GetProperty("errorCode").GetString() == "runtime_preview_consent_required");
    }

    [Fact(DisplayName = "Planner policy should allow DeploymentPrepare only for runtime_package_precheck")]
    public void PlannerPolicy_ShouldAllowOnlyRuntimePackagePrecheckForDeploymentPrepare()
    {
        var policy = new AgentToolCallPolicy();

        policy.ValidateToolName("runtime_package_precheck").Allowed.Should().BeTrue();
        policy.ValidateToolName("deploy_runtime_package").Allowed.Should().BeFalse();
    }

    [Fact(DisplayName = "Planner should return controlled failure when MaxToolRounds is exceeded")]
    public async Task PlannerMode_ShouldReturnControlledFailureWhenMaxToolRoundsExceeded()
    {
        var service = CreatePlannerService(
            new DelegatePlannerCompletionSource((_, _) => ToolCall("list_operator_catalog", new { })),
            new AgentGenerateFlowOptions
            {
                Mode = AiAgentGenerateFlowModes.Planner,
                FallbackToScriptedOnPlannerFailure = false
            },
            new VisionAgentLoopOptions
            {
                MaxToolRounds = 1,
                MaxToolCallsPerRound = 4,
                MaxToolResultChars = 64_000
            });

        var result = await service.GenerateFlowAsync(PlannerRequest("loop forever"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("MaxToolRounds=1");
    }

    [Fact(DisplayName = "Planner tool result should be truncated before feeding back to completion source")]
    public async Task PlannerLoop_ShouldTruncateLargeToolResults()
    {
        var latestToolResultMessage = string.Empty;
        var source = new DelegatePlannerCompletionSource((request, index) =>
        {
            if (index == 0)
            {
                return ToolCall("get_flow_template_skeleton", new { templateId = "hole_distance_measurement" });
            }

            latestToolResultMessage = request.Messages.Last().Content;
            return "done";
        });
        var loopOptions = new VisionAgentLoopOptions
        {
            MaxToolRounds = 2,
            MaxToolCallsPerRound = 4,
            MaxToolResultChars = 80
        };
        var loop = CreateLoop(loopOptions);
        var planner = CreatePlannerOnly(source);

        var result = await loop.RunAsync(new VisionAgentLoopRequest
        {
            UserPrompt = "hole distance",
            ToolContext = new VisionAgentToolContext
            {
                UserDescription = "hole distance",
                MaxToolResultChars = loopOptions.MaxToolResultChars,
                AllowedPermissions = new HashSet<VisionAgentToolPermission>
                {
                    VisionAgentToolPermission.ReadOnly,
                    VisionAgentToolPermission.Simulation,
                    VisionAgentToolPermission.DeploymentPrepare
                }
            },
            CompleteAsync = (messages, ct) => planner.CompleteAsync(new AgentPlannerCompletionRequest
            {
                GenerationRequest = PlannerRequest("hole distance"),
                Messages = messages
            }, ct)
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        latestToolResultMessage.Should().Contain("\"truncated\":true");
    }

    [Fact(DisplayName = "GenerateFlow response should map planner fields")]
    public async Task GenerateFlowMessageHandler_ShouldMapPlannerFields()
    {
        var generationService = Substitute.For<IAiFlowGenerationService>();
        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<Action<AiStreamChunk>?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<GenerateFlowAttachmentReport>?>())
            .Returns(Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                Flow = new OperatorFlowDto(),
                GenerationMode = "agent_planner",
                MissingResources = [new AiMissingResourceInfo { ResourceType = "model_path", ResourceKey = "op.ModelPath", Description = "missing" }],
                PendingActions = [new { actionType = "provide_missing_resource" }],
                ValidationPreview = new { deploymentPrecheck = new { workflowDraftAllowed = true } },
                ToolTrace = [new { toolName = "validate_flow", success = true }]
            }));
        var handler = new GenerateFlowMessageHandler(
            generationService,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>());

        var json = await handler.HandleAsync(
            "Check wire order on the harness terminal from camera. OK when order is correct, NG otherwise. Use model strategy.",
            useVisionAgentGenerateFlow: true,
            agentGenerateFlowMode: AiAgentGenerateFlowModes.Planner);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("generationMode").GetString().Should().Be("agent_planner");
        doc.RootElement.GetProperty("missingResources").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("pendingActions").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("toolTrace").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("validationPreview").GetProperty("deploymentPrecheck")
            .GetProperty("workflowDraftAllowed")
            .GetBoolean()
            .Should()
            .BeTrue();
    }

    [Fact(DisplayName = "Planner source guard should exclude hardware network process and RuntimePreview APIs")]
    public void SourceGuard_ShouldExcludeHardwareNetworkProcessAndRuntimePreviewApis()
    {
        var source = ReadSourceUnder(Path.Combine(GetProductRoot(), "src", "ClearVision.Product.Infrastructure", "AI", "Agent")) +
                     ReadSourceUnder(Path.Combine(GetProductRoot(), "src", "ClearVision.Product.Infrastructure", "AI", "Tools"));
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
            "execute_command"
        };

        forbidden.Should().OnlyContain(fragment =>
            !source.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static VisionAgentGenerateFlowService CreatePlannerService(
        IVisionAgentPlannerCompletionSource completionSource,
        AgentGenerateFlowOptions? agentOptions = null,
        VisionAgentLoopOptions? loopOptions = null)
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
        var policy = new AgentToolCallPolicy();
        var planner = new VisionAgentPlannerService(
            completionSource,
            parser,
            policy,
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

    private static IVisionAgentPlannerService CreatePlannerOnly(IVisionAgentPlannerCompletionSource completionSource)
    {
        var parser = new VisionAgentProtocolParser();
        return new VisionAgentPlannerService(
            completionSource,
            parser,
            new AgentToolCallPolicy(),
            new AgentPlannerPromptBuilder());
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
            new RuntimePackagePrecheckTool(),
            new RuntimePreviewCaptureStubTool(),
            new RuntimePreviewReplayStubTool()
        ]);
    }

    private static IVisionAgentPlannerCompletionSource NewTemplatePlanner(string templateId)
    {
        return new DelegatePlannerCompletionSource((request, index) => index switch
        {
            0 => ToolCalls(
                ("list_operator_catalog", new { keyword = request.GenerationRequest.Description }),
                ("get_operator_schema", new { operatorType = "ImageAcquisition" }),
                ("match_flow_template", new { request = request.GenerationRequest.Description }),
                ("inspect_current_flow", new { existingFlowJson = request.GenerationRequest.ExistingFlowJson })),
            1 => ToolCall("get_flow_template_skeleton", new { templateId }),
            2 => ToolCall("validate_flow", new { flow = request.FlowDraft }),
            3 => ToolCall("dryrun_flow", new { flow = request.FlowDraft }),
            4 => ToolCall("runtime_package_precheck", new
            {
                flow = request.FlowDraft,
                validationSummary = request.ValidationSummary,
                dryRunSummary = request.DryRunSummary
            }),
            _ => "Planner final."
        });
    }

    private static IVisionAgentPlannerCompletionSource EditPlanner(object editedFlow)
    {
        return new DelegatePlannerCompletionSource((request, index) => index switch
        {
            0 => ToolCall("inspect_current_flow", new { existingFlowJson = request.GenerationRequest.ExistingFlowJson }),
            1 => ToolCall("validate_flow", new { flow = editedFlow }),
            2 => ToolCall("dryrun_flow", new { flow = editedFlow }),
            3 => ToolCall("runtime_package_precheck", new
            {
                flow = editedFlow,
                validationSummary = request.ValidationSummary,
                dryRunSummary = request.DryRunSummary
            }),
            _ => "Planner final."
        });
    }

    private static AiFlowGenerationRequest PlannerRequest(string description, string? existingFlowJson = null)
    {
        return new AiFlowGenerationRequest(description, ExistingFlowJson: existingFlowJson)
        {
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Planner
        };
    }

    private static AiFlowGenerationService CreateAiFlowGenerationService(
        IVisionAgentGenerateFlowService agentGenerateFlowService,
        AgentGenerateFlowOptions agentOptions)
    {
        var conversationService = Substitute.For<IConversationalFlowService>();
        conversationService.PrepareContext(Arg.Any<AiFlowGenerationRequest>())
            .Returns(new ConversationContext
            {
                SessionId = "session",
                Intent = ConversationIntent.New,
                Mode = GenerateFlowMode.Auto
            });
        conversationService.GetSession("session").Returns(new ConversationSession { SessionId = "session" });
        var turnRouter = Substitute.For<IAiTurnRouter>();
        turnRouter.Route(Arg.Any<AiTurnRouteRequest>())
            .Returns(new AiTurnRoute(
                AiTurnIntents.ChatOrHelp,
                AiInteractionStates.Idle,
                AiRouterConfidence.High,
                ShouldShortCircuit: true,
                Reply: "hello"));
        var promptVersionManager = Substitute.For<IPromptVersionManager>();
        promptVersionManager.GetActiveVersionAsync().Returns(Task.FromResult(new PromptVersion
        {
            Id = Guid.NewGuid(),
            Name = "Test Prompt",
            Content = "test"
        }));
        var operatorFactory = Substitute.For<IOperatorFactory>();

        return new AiFlowGenerationService(
            new AiGenerationOrchestrator(
                Substitute.For<IAiModelSelector>(),
                Substitute.For<IAiConnectorFactory>()),
            new PromptBuilder(operatorFactory),
            conversationService,
            Substitute.For<IAiFlowValidator>(),
            new AutoLayoutService(),
            operatorFactory,
            Substitute.For<IFlowTemplateService>(),
            Substitute.For<IScenarioMatcher>(),
            Substitute.For<IRequirementBriefExtractor>(),
            turnRouter,
            Substitute.For<ITemplateConstraintValidator>(),
            new AiFlowResponseParser(),
            new DryRunService(Substitute.For<IFlowExecutionService>()),
            Substitute.For<IHostEnvironment>(),
            promptVersionManager,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<AiFlowGenerationService>>(),
            Options.Create(agentOptions),
            agentGenerateFlowService);
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

    private static object AddResultOutputFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "<pending-camera-binding>" }),
                Operator("op_match", "TemplateMatching", new Dictionary<string, string> { ["TemplatePath"] = "template://old" }),
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

    private static object ReplaceTemplatePathFlow(string value)
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "<pending-camera-binding>" }),
                Operator("op_match", "TemplateMatching", new Dictionary<string, string> { ["TemplatePath"] = value }),
                Operator("op_judge", "ResultJudgment")
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_match", "Image"),
                Connection("op_match", "Score", "op_judge", "Input")
            }
        };
    }

    private static object BrokenConnectionFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "cam_1" }),
                Operator("op_match", "TemplateMatching", new Dictionary<string, string> { ["TemplatePath"] = "template://fixture" })
            },
            connections = new object[]
            {
                Connection("op_missing", "Image", "op_match", "Image")
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
        return ToolCalls((name, arguments));
    }

    private static string ToolCalls(params (string Name, object Arguments)[] calls)
    {
        return JsonSerializer.Serialize(new
        {
            kind = "tool_call",
            toolCalls = calls.Select((call, index) => new
            {
                id = $"call_{index + 1}",
                name = call.Name,
                arguments = call.Arguments
            })
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

    private static IReadOnlyList<string> Codes(JsonElement payload, string propertyName)
    {
        return payload.GetProperty(propertyName)
            .EnumerateArray()
            .Select(issue => issue.GetProperty("code").GetString() ?? string.Empty)
            .ToList();
    }

    private static JsonElement Json(object? value)
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

    private sealed class DelegatePlannerCompletionSource : IVisionAgentPlannerCompletionSource
    {
        private readonly Func<AgentPlannerCompletionRequest, int, string> _next;
        private int _index;

        public DelegatePlannerCompletionSource(Func<AgentPlannerCompletionRequest, int, string> next)
        {
            _next = next;
        }

        public Task<string> CompleteAsync(
            AgentPlannerCompletionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_next(request, _index++));
        }
    }

    private sealed class FakeAgentGenerateFlowService : IVisionAgentGenerateFlowService
    {
        private readonly Func<AiFlowGenerationRequest, Task<AiFlowGenerationResult>> _handler;

        public FakeAgentGenerateFlowService(Func<AiFlowGenerationRequest, Task<AiFlowGenerationResult>> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public Task<AiFlowGenerationResult> GenerateFlowAsync(
            AiFlowGenerationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return _handler(request);
        }
    }
}
