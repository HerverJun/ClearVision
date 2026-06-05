using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Tests.AI.VisionAgentRuntimePreview;

public sealed class VisionAgentRuntimePreviewTests
{
    [Fact(DisplayName = "Default GenerateFlow request should not allow RuntimePreview")]
    public void DefaultGenerateFlowRequest_ShouldNotAllowRuntimePreview()
    {
        var policy = new AgentToolCallPolicy();
        var request = new AiFlowGenerationRequest("preview");

        request.RuntimePreviewConsent.Should().BeFalse();
        RuntimePreviewPermissionGate.HasConsent(request).Should().BeFalse();
        policy.ValidateToolName(RuntimePreviewPermissionGate.CaptureToolName).Allowed.Should().BeFalse();
        policy.ListAllowedToolNames().Should().NotContain(RuntimePreviewPermissionGate.CaptureToolName);
    }

    [Fact(DisplayName = "Developer Agent without consent should reject capture_test_frame with pendingAction")]
    public async Task DeveloperAgentWithoutConsent_ShouldRejectCaptureWithPendingAction()
    {
        var service = CreatePlannerService(new DelegatePlannerCompletionSource((_, index) => index switch
        {
            0 => ToolCall(RuntimePreviewPermissionGate.CaptureToolName, new { cameraBindingId = "mock-cam" }),
            _ => FinalWorkflowDraft(TemplateMatchingFlow())
        }));

        var result = await service.GenerateFlowAsync(PlannerRequest("preview without consent"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PendingActions.Should().Contain(action =>
            Json(action).GetRawText().Contains("AuthorizeRuntimePreview", StringComparison.OrdinalIgnoreCase));
        var trace = Trace(result).Single(item =>
            item.GetProperty("toolName").GetString() == RuntimePreviewPermissionGate.CaptureToolName);
        trace.GetProperty("success").GetBoolean().Should().BeFalse();
        trace.GetProperty("errorCode").GetString().Should().Be(RuntimePreviewPermissionGate.ConsentRequiredErrorCode);
        trace.GetProperty("permission").GetString().Should().Be(nameof(VisionAgentToolPermission.RuntimePreview));
    }

    [Fact(DisplayName = "Developer Agent with consent should allow capture_test_frame stub")]
    public async Task DeveloperAgentWithConsent_ShouldAllowCaptureStub()
    {
        var service = CreatePlannerService(new DelegatePlannerCompletionSource((_, index) => index switch
        {
            0 => ToolCall(RuntimePreviewPermissionGate.CaptureToolName, new { cameraBindingId = "mock-cam", operatorTempId = "op_cam" }),
            _ => FinalWorkflowDraft(TemplateMatchingFlow())
        }));

        var result = await service.GenerateFlowAsync(
            PlannerRequest("preview with consent") with { RuntimePreviewConsent = true },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var trace = Trace(result).Single(item =>
            item.GetProperty("toolName").GetString() == RuntimePreviewPermissionGate.CaptureToolName);
        trace.GetProperty("success").GetBoolean().Should().BeTrue();
        trace.GetProperty("permission").GetString().Should().Be(nameof(VisionAgentToolPermission.RuntimePreview));
        trace.GetProperty("permissionDecision").GetProperty("runtimePreviewConsent").GetBoolean().Should().BeTrue();
        Json(result.ValidationPreview).GetProperty("runtimePreview").GetProperty("previewReady").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "Developer Agent with consent should allow replay_flow_with_frame stub")]
    public async Task DeveloperAgentWithConsent_ShouldAllowReplayStub()
    {
        var flow = TemplateMatchingFlow();
        var service = CreatePlannerService(new DelegatePlannerCompletionSource((_, index) => index switch
        {
            0 => ToolCall(RuntimePreviewPermissionGate.ReplayToolName, new { frameId = "stub-frame-1", flow }),
            _ => FinalWorkflowDraft(flow)
        }));

        var result = await service.GenerateFlowAsync(
            PlannerRequest("replay with consent") with { RuntimePreviewConsent = true },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var preview = Json(result.ValidationPreview).GetProperty("runtimePreview");
        preview.GetProperty("previewReady").GetBoolean().Should().BeTrue();
        preview.GetProperty("replaySummary").GetProperty("replaySucceeded").GetBoolean().Should().BeTrue();
        Trace(result).Single(item => item.GetProperty("toolName").GetString() == RuntimePreviewPermissionGate.ReplayToolName)
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue();
    }

    [Fact(DisplayName = "Developer Agent without consent should reject replay_flow_with_frame with pendingAction")]
    public async Task DeveloperAgentWithoutConsent_ShouldRejectReplayWithPendingAction()
    {
        var service = CreatePlannerService(new DelegatePlannerCompletionSource((_, index) => index switch
        {
            0 => ToolCall(RuntimePreviewPermissionGate.ReplayToolName, new { frameId = "stub-frame-1", flow = TemplateMatchingFlow() }),
            _ => FinalWorkflowDraft(TemplateMatchingFlow())
        }));

        var result = await service.GenerateFlowAsync(PlannerRequest("replay without consent"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PendingActions.Should().Contain(action =>
            Json(action).GetRawText().Contains("AuthorizeRuntimePreview", StringComparison.OrdinalIgnoreCase));
        Trace(result).Single(item => item.GetProperty("toolName").GetString() == RuntimePreviewPermissionGate.ReplayToolName)
            .GetProperty("errorCode")
            .GetString()
            .Should()
            .Be(RuntimePreviewPermissionGate.ConsentRequiredErrorCode);
    }

    [Fact(DisplayName = "RuntimePreview stubs should not include image binary")]
    public async Task RuntimePreviewStubs_ShouldNotIncludeImageBinary()
    {
        var registry = CreateRegistry();
        var result = await registry.ExecuteAsync(
            RuntimePreviewPermissionGate.CaptureToolName,
            RuntimePreviewContext(),
            Args(new { cameraBindingId = "mock-cam" }),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("binaryIncluded").GetBoolean().Should().BeFalse();
        var artifact = payload.GetProperty("artifacts").EnumerateArray().Single();
        artifact.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
        artifact.GetProperty("binaryIncluded").GetBoolean().Should().BeFalse();
        artifact.GetProperty("byteLength").GetInt64().Should().Be(0);
        payload.GetRawText().ToLowerInvariant().Should().NotContain("base64");
    }

    [Fact(DisplayName = "RuntimePreview policy should only whitelist tools when consent is present")]
    public void RuntimePreviewPolicy_ShouldRequireConsent()
    {
        var policy = new AgentToolCallPolicy();

        policy.ValidateToolName(RuntimePreviewPermissionGate.CaptureToolName).Allowed.Should().BeFalse();
        policy.ValidateToolName(RuntimePreviewPermissionGate.CaptureToolName, runtimePreviewConsent: true).Allowed.Should().BeTrue();
        policy.ListAllowedToolNames().Should().NotContain(RuntimePreviewPermissionGate.ReplayToolName);
        policy.ListAllowedToolNames(runtimePreviewConsent: true).Should().Contain(RuntimePreviewPermissionGate.ReplayToolName);
    }

    [Fact(DisplayName = "ToolTrace should record RuntimePreview permission consent and denial reason")]
    public async Task ToolTrace_ShouldRecordRuntimePreviewDecision()
    {
        var service = CreatePlannerService(new DelegatePlannerCompletionSource((_, index) => index switch
        {
            0 => ToolCall(RuntimePreviewPermissionGate.CaptureToolName, new { cameraBindingId = "mock-cam" }),
            _ => FinalWorkflowDraft(TemplateMatchingFlow())
        }));

        var result = await service.GenerateFlowAsync(PlannerRequest("trace preview"), CancellationToken.None);

        var trace = Trace(result).Single(item =>
            item.GetProperty("toolName").GetString() == RuntimePreviewPermissionGate.CaptureToolName);
        trace.GetProperty("permission").GetString().Should().Be(nameof(VisionAgentToolPermission.RuntimePreview));
        trace.GetProperty("permissionDecision").GetProperty("runtimePreviewConsent").GetBoolean().Should().BeFalse();
        trace.GetProperty("permissionDecision").GetProperty("reason").GetString().Should().Be(RuntimePreviewPermissionGate.ConsentRequiredErrorCode);
    }

    [Fact(DisplayName = "WorkflowDraftAllowed should not be blocked by missing RuntimePreview")]
    public async Task WorkflowDraftAllowed_ShouldNotBeBlockedByMissingRuntimePreview()
    {
        var flow = TemplateMatchingFlow();
        var service = CreatePlannerService(new DelegatePlannerCompletionSource((request, index) => index switch
        {
            0 => ToolCall(RuntimePreviewPermissionGate.CaptureToolName, new { cameraBindingId = "mock-cam" }),
            1 => ToolCall("validate_flow", new { flow }),
            2 => ToolCall("dryrun_flow", new { flow }),
            3 => ToolCall("runtime_package_precheck", new
            {
                flow,
                validationSummary = request.ValidationSummary,
                dryRunSummary = request.DryRunSummary
            }),
            _ => FinalWorkflowDraft(flow)
        }));

        var result = await service.GenerateFlowAsync(PlannerRequest("template preview precheck"), CancellationToken.None);

        result.Success.Should().BeTrue();
        var precheck = Json(result.ValidationPreview).GetProperty("deploymentPrecheck");
        precheck.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        result.PendingActions.Should().Contain(action =>
            Json(action).GetRawText().Contains("AuthorizeRuntimePreview", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "GenerateFlowMessageHandler should pass RuntimePreview consent into request")]
    public async Task GenerateFlowMessageHandler_ShouldPassRuntimePreviewConsent()
    {
        AiFlowGenerationRequest? captured = null;
        var generationService = Substitute.For<IAiFlowGenerationService>();
        generationService.GenerateFlowAsync(
                Arg.Do<AiFlowGenerationRequest>(request => captured = request),
                Arg.Any<Action<string>?>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>?>())
            .Returns(Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                Flow = new OperatorFlowDto()
            }));
        var handler = new GenerateFlowMessageHandler(
            generationService,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>());

        _ = await handler.HandleAsync(
            "preview",
            useVisionAgentGenerateFlow: true,
            agentGenerateFlowMode: AiAgentGenerateFlowModes.Planner,
            runtimePreviewConsent: true);

        captured.Should().NotBeNull();
        captured!.UseVisionAgentGenerateFlow.Should().BeTrue();
        captured.AgentGenerateFlowMode.Should().Be(AiAgentGenerateFlowModes.Planner);
        captured.RuntimePreviewConsent.Should().BeTrue();
    }

    [Fact(DisplayName = "RuntimePreview source guard should avoid real camera station image model network and process APIs")]
    public void SourceGuard_ShouldAvoidRealRuntimeAccess()
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
            "powershell.exe",
            "cmd.exe",
            "execute_command",
            "StationRuntimeClient"
        };

        forbidden.Should().OnlyContain(fragment =>
            !source.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static VisionAgentGenerateFlowService CreatePlannerService(
        IVisionAgentPlannerCompletionSource completionSource)
    {
        var loopOptions = new VisionAgentLoopOptions
        {
            MaxToolRounds = 8,
            MaxToolCallsPerRound = 4,
            MaxToolResultChars = 64_000
        };
        var parser = new VisionAgentProtocolParser();
        var planner = new VisionAgentPlannerService(
            completionSource,
            parser,
            new AgentToolCallPolicy(),
            new AgentPlannerPromptBuilder());

        return new VisionAgentGenerateFlowService(
            new VisionAgentLoop(
                CreateRegistry(),
                new VisionAgentProtocolParser(),
                new AgentPromptBuilder(),
                Options.Create(loopOptions)),
            Options.Create(loopOptions),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentGenerateFlowService>>(),
            Options.Create(new AgentGenerateFlowOptions
            {
                Mode = AiAgentGenerateFlowModes.Planner,
                FallbackToScriptedOnPlannerFailure = false
            }),
            planner,
            parser,
            new AgentWorkflowDraftEditor());
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

    private static VisionAgentToolContext RuntimePreviewContext()
    {
        return new VisionAgentToolContext
        {
            RuntimePreviewConsent = true,
            AllowedPermissions = new HashSet<VisionAgentToolPermission>
            {
                VisionAgentToolPermission.ReadOnly,
                VisionAgentToolPermission.Simulation,
                VisionAgentToolPermission.RuntimePreview
            }
        };
    }

    private static AiFlowGenerationRequest PlannerRequest(string description)
    {
        return new AiFlowGenerationRequest(description)
        {
            UseVisionAgentGenerateFlow = true,
            AgentGenerateFlowMode = AiAgentGenerateFlowModes.Planner
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

    private static JsonElement Args(object? value = null)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value ?? new { }));
        return doc.RootElement.Clone();
    }

    private static JsonElement Json(object? value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return doc.RootElement.Clone();
    }

    private static IReadOnlyList<JsonElement> Trace(AiFlowGenerationResult result)
    {
        return Json(result.ToolTrace)
            .EnumerateArray()
            .Select(item => item.Clone())
            .ToList();
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
}
