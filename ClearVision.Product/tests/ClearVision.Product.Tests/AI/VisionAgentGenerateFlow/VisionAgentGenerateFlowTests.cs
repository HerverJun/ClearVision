using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.DryRun;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Infrastructure.AI.Tools;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Tests.AI.VisionAgentGenerateFlow;

public sealed class VisionAgentGenerateFlowTests
{
    [Fact(DisplayName = "Default GenerateFlow should not trigger VisionAgentLoop")]
    public async Task DefaultGenerateFlow_ShouldNotTriggerAgentService()
    {
        var agent = new FakeAgentGenerateFlowService(_ => throw new InvalidOperationException("agent should not run"));
        var service = CreateAiFlowGenerationService(
            agent,
            new AgentGenerateFlowOptions { Enabled = true });

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest("hello"));

        result.Success.Should().BeTrue();
        agent.CallCount.Should().Be(0);
        result.ToolTrace.Should().BeEmpty();
    }

    [Fact(DisplayName = "Explicit Agent GenerateFlow should trigger controlled agent branch")]
    public async Task ExplicitAgentGenerateFlow_ShouldTriggerAgentBranch()
    {
        var agent = new FakeAgentGenerateFlowService(_ => Task.FromResult(new AiFlowGenerationResult
        {
            Success = true,
            CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
            Flow = new OperatorFlowDto(),
            ToolTrace = [new { toolName = "runtime_package_precheck", success = true }]
        }));
        var service = CreateAiFlowGenerationService(
            agent,
            new AgentGenerateFlowOptions { Enabled = true });

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest("wire sequence")
        {
            UseVisionAgentGenerateFlow = true
        });

        result.Success.Should().BeTrue();
        agent.CallCount.Should().Be(1);
        result.ToolTrace.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "Controlled agent should call ReadOnly Simulation and Precheck tools")]
    public async Task AgentGenerateFlow_ShouldCallToolChain()
    {
        var result = await CreateVisionAgentGenerateFlowService().GenerateFlowAsync(
            AgentRequest("template matching alignment"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var trace = Trace(result);
        trace.Select(item => item.GetProperty("toolName").GetString()).Should().Contain([
            "list_operator_catalog",
            "get_operator_schema",
            "match_flow_template",
            "inspect_current_flow",
            "get_flow_template_skeleton",
            "validate_flow",
            "dryrun_flow",
            "runtime_package_precheck"]);
        trace.Take(5)
            .Select(item => item.GetProperty("permission").GetString())
            .Should()
            .OnlyContain(permission => permission == nameof(VisionAgentToolPermission.ReadOnly));
        trace.TakeLast(3)
            .Select(item => item.GetProperty("toolName").GetString())
            .Should()
            .Equal("validate_flow", "dryrun_flow", "runtime_package_precheck");
    }

    [Fact(DisplayName = "Wire sequence request should generate draft and ModelPath pending action")]
    public async Task AgentGenerateFlow_WireSequence_ShouldReturnModelPathPendingAction()
    {
        var result = await CreateVisionAgentGenerateFlowService().GenerateFlowAsync(
            AgentRequest("terminal wire sequence inspection"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.DeepLearning);
        result.MissingResources.Select(item => item.ResourceType).Should().Contain("model_path");
        Json(result.PendingActions).GetRawText().Should().Contain("ModelPath");
    }

    [Fact(DisplayName = "Template matching request should generate draft and TemplatePath pending action")]
    public async Task AgentGenerateFlow_TemplateMatching_ShouldReturnTemplatePathPendingAction()
    {
        var result = await CreateVisionAgentGenerateFlowService().GenerateFlowAsync(
            AgentRequest("template matching alignment for bracket"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.TemplateMatching);
        result.MissingResources.Select(item => item.ResourceType).Should().Contain("template_path");
        Json(result.PendingActions).GetRawText().Should().Contain("TemplatePath");
    }

    [Fact(DisplayName = "Hole distance measurement request should generate measurement draft")]
    public async Task AgentGenerateFlow_HoleDistance_ShouldReturnMeasurementDraft()
    {
        var result = await CreateVisionAgentGenerateFlowService().GenerateFlowAsync(
            AgentRequest("hole distance measurement in mm"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.CircleMeasurement);
        Flow(result).Operators.Select(op => op.Type).Should().Contain(OperatorType.Measurement);
    }

    [Fact(DisplayName = "Missing resources should not block workflow draft")]
    public async Task AgentGenerateFlow_MissingResources_ShouldAllowWorkflowDraft()
    {
        var result = await CreateVisionAgentGenerateFlowService().GenerateFlowAsync(
            AgentRequest("template matching alignment"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Flow.Should().NotBeNull();
        var precheck = ValidationPreview(result).GetProperty("deploymentPrecheck");
        precheck.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        precheck.GetProperty("readyForDeployment").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "Existing flow structural error should enter validationPreview blockingIssues")]
    public async Task AgentGenerateFlow_StructuralError_ShouldEnterValidationPreview()
    {
        var result = await CreateVisionAgentGenerateFlowService().GenerateFlowAsync(
            AgentRequest(
                "validate existing flow",
                existingFlowJson: JsonSerializer.Serialize(BrokenConnectionFlow())),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var structural = ValidationPreview(result).GetProperty("structuralValidation");
        Codes(structural, "blockingIssues").Should().Contain("broken_connection_temp_id");
    }

    [Fact(DisplayName = "Deployment precheck should not deploy create package or touch station")]
    public async Task AgentGenerateFlow_Precheck_ShouldNeverDeploy()
    {
        var result = await CreateVisionAgentGenerateFlowService().GenerateFlowAsync(
            AgentRequest("terminal wire sequence inspection"),
            CancellationToken.None);

        var precheck = ValidationPreview(result).GetProperty("deploymentPrecheck");
        precheck.GetProperty("readyForDeployment").GetBoolean().Should().BeFalse();
        precheck.GetProperty("deployed").GetBoolean().Should().BeFalse();
        precheck.GetProperty("packageCreated").GetBoolean().Should().BeFalse();
        precheck.GetProperty("stationTouched").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "Agent failure should return controlled error when fallback is disabled")]
    public async Task AgentGenerateFlowFailure_ShouldReturnControlledError()
    {
        var service = CreateAiFlowGenerationService(
            new FakeAgentGenerateFlowService(_ => throw new InvalidOperationException("scripted failure")),
            new AgentGenerateFlowOptions
            {
                Enabled = true,
                FallbackToLegacyOnFailure = false
            });

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest("wire sequence")
        {
            UseVisionAgentGenerateFlow = true
        });

        result.Success.Should().BeFalse();
        result.FailureType.Should().Be(AiFlowGenerationResult.FailureTypeSystemError);
        result.ErrorMessage.Should().Contain("Vision Agent GenerateFlow failed");
    }

    [Fact(DisplayName = "GenerateFlow response should map toolTrace pendingActions missingResources and validationPreview")]
    public async Task GenerateFlowMessageHandler_ShouldMapAgentFields()
    {
        var flow = new OperatorFlowDto();
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
                Flow = flow,
                MissingResources = [new AiMissingResourceInfo { ResourceType = "model_path", ResourceKey = "op.ModelPath", Description = "missing" }],
                PendingActions = [new { actionType = "provide_missing_resource" }],
                ValidationPreview = new { deploymentPrecheck = new { workflowDraftAllowed = true } },
                ToolTrace = [new { toolName = "validate_flow", success = true }]
            }));
        var handler = new GenerateFlowMessageHandler(
            generationService,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>());

        var json = await handler.HandleAsync("wire", useVisionAgentGenerateFlow: true);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("missingResources").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("pendingActions").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("toolTrace").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("validationPreview").GetProperty("deploymentPrecheck")
            .GetProperty("workflowDraftAllowed")
            .GetBoolean()
            .Should()
            .BeTrue();
    }

    [Fact(DisplayName = "Controlled agent source guard should exclude RuntimePreview hardware network and process APIs")]
    public void SourceGuard_ShouldExcludeRuntimePreviewHardwareNetworkAndProcess()
    {
        var source = ReadSourceUnder(Path.Combine(
            GetProductRoot(),
            "src",
            "ClearVision.Product.Infrastructure",
            "AI",
            "Agent")) +
                     ReadSourceUnder(Path.Combine(
                         GetProductRoot(),
                         "src",
                         "ClearVision.Product.Infrastructure",
                         "AI",
                         "Tools"));
        var forbidden = new[]
        {
            "capture_test_frame",
            "replay_flow_with_frame",
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

    [Fact(DisplayName = "Mainline guard should keep Agent loop out of default GenerateFlow and frontend")]
    public void MainlineGuard_ShouldKeepAgentLoopOutOfDefaultGenerateFlowAndFrontend()
    {
        var productRoot = GetProductRoot();
        var generateFlowService = File.ReadAllText(Path.Combine(
            productRoot,
            "src",
            "ClearVision.Product.Infrastructure",
            "AI",
            "AiFlowGenerationService.cs"));
        var frontendSource = ReadSourceUnder(Path.Combine(
            productRoot,
            "src",
            "ClearVision.Product.Desktop",
            "wwwroot",
            "src"));

        generateFlowService.Should().NotContain("VisionAgentLoop");
        generateFlowService.Should().Contain("ShouldRunAgentGenerateFlow");
        generateFlowService.Should().Contain("request.UseVisionAgentGenerateFlow");
        frontendSource.Should().NotContain("useVisionAgentGenerateFlow");
        frontendSource.Should().NotContain("capture_test_frame");
        frontendSource.Should().NotContain("replay_flow_with_frame");
        frontendSource.Should().NotContain("runtime_package_precheck");
    }

    [Fact(DisplayName = "Controlled agent should not be enabled by default in options")]
    public void AgentGenerateFlowOptions_ShouldDefaultDisabled()
    {
        new AgentGenerateFlowOptions().Enabled.Should().BeFalse();
    }

    private static AiFlowGenerationRequest AgentRequest(string description, string? existingFlowJson = null)
    {
        return new AiFlowGenerationRequest(description, ExistingFlowJson: existingFlowJson)
        {
            UseVisionAgentGenerateFlow = true
        };
    }

    private static VisionAgentGenerateFlowService CreateVisionAgentGenerateFlowService()
    {
        var loopOptions = new VisionAgentLoopOptions
        {
            MaxToolRounds = 8,
            MaxToolCallsPerRound = 4,
            MaxToolResultChars = 64_000
        };
        var registry = new VisionAgentToolRegistry(
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
        var loop = new VisionAgentLoop(
            registry,
            new VisionAgentProtocolParser(),
            new AgentPromptBuilder(),
            Options.Create(loopOptions));

        return new VisionAgentGenerateFlowService(
            loop,
            Options.Create(loopOptions),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentGenerateFlowService>>());
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
        var flowExecutionService = Substitute.For<IFlowExecutionService>();

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
            new DryRunService(flowExecutionService),
            Substitute.For<IHostEnvironment>(),
            promptVersionManager,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<AiFlowGenerationService>>(),
            Options.Create(agentOptions),
            agentGenerateFlowService);
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
