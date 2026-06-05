using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ClearVision.Product.Tests.AI.VisionAgentRuntimePreviewAdapter;

public sealed class VisionAgentRuntimePreviewAdapterTests
{
    [Fact(DisplayName = "RuntimePreview adapter tools should reject capture and replay without consent")]
    public async Task RuntimePreviewTools_ShouldRejectCaptureAndReplayWithoutConsent()
    {
        var registry = CreateRegistry();
        var context = new VisionAgentToolContext
        {
            AllowedPermissions = new HashSet<VisionAgentToolPermission>
            {
                VisionAgentToolPermission.RuntimePreview
            }
        };

        var capture = await registry.ExecuteAsync(
            RuntimePreviewPermissionGate.CaptureToolName,
            context,
            Args(new { cameraBindingId = "mock-cam" }),
            CancellationToken.None);
        var replay = await registry.ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            context,
            Args(new { frameId = "offline-frame", flow = ValidFlow() }),
            CancellationToken.None);

        capture.Success.Should().BeFalse();
        replay.Success.Should().BeFalse();
        capture.ErrorCode.Should().Be(RuntimePreviewPermissionGate.ConsentRequiredErrorCode);
        replay.ErrorCode.Should().Be(RuntimePreviewPermissionGate.ConsentRequiredErrorCode);
        capture.PendingActions.Should().Contain(action => action.ActionType == "AuthorizeRuntimePreview");
        replay.PendingActions.Should().Contain(action => action.ActionType == "AuthorizeRuntimePreview");
    }

    [Fact(DisplayName = "Authorized capture should call OfflineRuntimePreviewAdapter")]
    public async Task AuthorizedCapture_ShouldCallOfflineAdapter()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.CaptureToolName,
            RuntimePreviewContext(),
            Args(new { cameraBindingId = "mock-cam", operatorTempId = "op_cam" }),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("adapterName").GetString().Should().Be(OfflineRuntimePreviewAdapter.AdapterName);
        payload.GetProperty("previewMode").GetString().Should().Be(RuntimePreviewModes.OfflineFixture);
        payload.GetProperty("previewReady").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "Authorized replay should call OfflineRuntimePreviewAdapter")]
    public async Task AuthorizedReplay_ShouldCallOfflineAdapter()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(),
            Args(new { frameId = "offline-frame", flow = ValidFlow() }),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = Json(result.Data);
        payload.GetProperty("adapterName").GetString().Should().Be(OfflineRuntimePreviewAdapter.AdapterName);
        payload.GetProperty("previewReady").GetBoolean().Should().BeTrue();
        payload.GetProperty("replaySummary").GetProperty("adapterName").GetString().Should().Be(OfflineRuntimePreviewAdapter.AdapterName);
    }

    [Fact(DisplayName = "RuntimePreview adapter registry should expose offline adapter deterministically")]
    public void AdapterRegistry_ShouldExposeOfflineAdapterDeterministically()
    {
        var registry = CreateAdapterRegistry();

        registry.ListAdapterNames().Should().Contain(OfflineRuntimePreviewAdapter.AdapterName);
        registry.TryGet("OFFLINE_RUNTIME_PREVIEW", out var adapter).Should().BeTrue();
        adapter.Name.Should().Be(OfflineRuntimePreviewAdapter.AdapterName);
        adapter.SupportedToolNames.Should().Contain(RuntimePreviewPermissionGate.CaptureToolName);
        adapter.SupportedToolNames.Should().Contain(RuntimePreviewPermissionGate.ReplayToolName);
    }

    [Fact(DisplayName = "Offline capture should return metadata-only frame artifact")]
    public async Task OfflineCapture_ShouldReturnMetadataOnlyFrameArtifact()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.CaptureToolName,
            RuntimePreviewContext(),
            Args(new { cameraBindingId = "mock-cam", operatorTempId = "op_cam" }),
            CancellationToken.None);

        var artifact = Json(result.Data)
            .GetProperty("artifacts")
            .EnumerateArray()
            .Single();
        artifact.GetProperty("artifactType").GetString().Should().Be("frame_metadata");
        artifact.GetProperty("metadataOnly").GetBoolean().Should().BeTrue();
        artifact.GetProperty("binaryIncluded").GetBoolean().Should().BeFalse();
        artifact.GetProperty("byteLength").GetInt64().Should().Be(0);
    }

    [Fact(DisplayName = "Offline capture should redact path-like camera binding in artifact metadata")]
    public async Task OfflineCapture_ShouldRedactPathLikeCameraBindingMetadata()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.CaptureToolName,
            RuntimePreviewContext(),
            Args(new { cameraBindingId = "C:\\secret\\camera.json", operatorTempId = "op_cam" }),
            CancellationToken.None);

        var artifact = Json(result.Data)
            .GetProperty("artifacts")
            .EnumerateArray()
            .Single();
        artifact.GetProperty("metadata").GetProperty("cameraBinding").GetString().Should().Be("<redacted>");
        var artifactRaw = artifact.GetRawText().ToLowerInvariant();
        artifactRaw.Should().NotContain("secret");
        artifactRaw.Should().NotContain(".json");
    }

    [Fact(DisplayName = "Offline replay should return operator result metadata artifacts")]
    public async Task OfflineReplay_ShouldReturnOperatorResultMetadata()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(),
            Args(new { frameId = "offline-frame", flow = ValidFlow() }),
            CancellationToken.None);

        var artifacts = Json(result.Data).GetProperty("artifacts").EnumerateArray().ToList();
        artifacts.Select(item => item.GetProperty("artifactType").GetString())
            .Should()
            .Contain("operator_result_metadata");
        artifacts.Should().Contain(item =>
            item.GetProperty("metadataOnly").GetBoolean() &&
            !item.GetProperty("binaryIncluded").GetBoolean() &&
            item.GetProperty("byteLength").GetInt64() == 0);
    }

    [Fact(DisplayName = "Offline replay should return previewReady false for structural errors")]
    public async Task OfflineReplay_ShouldReturnPreviewNotReadyForStructuralErrors()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(),
            Args(new { frameId = "offline-frame", flow = BrokenFlow() }),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        var payload = Json(result.Data);
        payload.GetProperty("previewReady").GetBoolean().Should().BeFalse();
        payload.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        payload.GetProperty("blockingIssues").EnumerateArray()
            .Select(item => item.GetProperty("code").GetString())
            .Should()
            .Contain("broken_connection_temp_id");
    }

    [Fact(DisplayName = "Offline replay should not block workflow draft when resources are missing")]
    public async Task OfflineReplay_ShouldAllowWorkflowDraftWhenResourcesAreMissing()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(),
            Args(new { frameId = "offline-frame", flow = ValidFlow() }),
            CancellationToken.None);

        var payload = Json(result.Data);
        payload.GetProperty("workflowDraftAllowed").GetBoolean().Should().BeTrue();
        payload.GetProperty("missingResources").EnumerateArray()
            .Select(item => item.GetProperty("parameterName").GetString())
            .Should()
            .Contain(["CameraBindingId", "TemplatePath"]);
        payload.GetProperty("previewReady").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "RuntimePreview artifacts should not include base64 image bytes or external paths")]
    public async Task RuntimePreviewArtifacts_ShouldNotLeakBinaryOrExternalPaths()
    {
        var flow = new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "C:\\secret\\camera.json" }),
                Operator("op_match", "TemplateMatching", new Dictionary<string, string> { ["TemplatePath"] = "C:\\secret\\template.png" })
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_match", "Image")
            }
        };

        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.ReplayToolName,
            RuntimePreviewContext(),
            Args(new { frameId = "offline-frame", flow }),
            CancellationToken.None);

        var artifactsRaw = Json(result.Data).GetProperty("artifacts").GetRawText().ToLowerInvariant();
        artifactsRaw.Should().NotContain("base64");
        artifactsRaw.Should().NotContain("secret");
        artifactsRaw.Should().NotContain(".png");
        artifactsRaw.Should().NotContain(".json");
    }

    [Fact(DisplayName = "RuntimePreview adapter registry should return controlled failure for unknown adapter")]
    public async Task AdapterRegistry_ShouldReturnControlledFailureForUnknownAdapter()
    {
        var result = await CreateRegistry().ExecuteAsync(
            RuntimePreviewPermissionGate.CaptureToolName,
            RuntimePreviewContext(),
            Args(new { adapterName = "missing_adapter", cameraBindingId = "mock-cam" }),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("runtime_preview_adapter_not_found");
        Json(result.Data).GetProperty("adapterName").GetString().Should().Be("missing_adapter");
    }

    [Fact(DisplayName = "ToolTrace should record RuntimePreview permission decision and adapterName")]
    public async Task ToolTrace_ShouldRecordPermissionDecisionAndAdapterName()
    {
        var flow = ValidFlow();
        var service = CreatePlannerService(new DelegatePlannerCompletionSource((_, index) => index switch
        {
            0 => ToolCall(RuntimePreviewPermissionGate.ReplayToolName, new { frameId = "offline-frame", flow }),
            _ => FinalWorkflowDraft(flow)
        }));

        var result = await service.GenerateFlowAsync(
            PlannerRequest("offline replay") with { RuntimePreviewConsent = true },
            CancellationToken.None);

        var trace = Trace(result).Single(item =>
            item.GetProperty("toolName").GetString() == RuntimePreviewPermissionGate.ReplayToolName);
        trace.GetProperty("permission").GetString().Should().Be(nameof(VisionAgentToolPermission.RuntimePreview));
        trace.GetProperty("adapterName").GetString().Should().Be(OfflineRuntimePreviewAdapter.AdapterName);
        trace.GetProperty("permissionDecision").GetProperty("runtimePreviewConsent").GetBoolean().Should().BeTrue();
        Json(result.ValidationPreview)
            .GetProperty("runtimePreview")
            .GetProperty("adapterName")
            .GetString()
            .Should()
            .Be(OfflineRuntimePreviewAdapter.AdapterName);
    }

    [Fact(DisplayName = "RuntimePreview adapter source guard should avoid real hardware station network and process APIs")]
    public void SourceGuard_ShouldAvoidDisallowedRuntimePreviewApis()
    {
        var source = ReadSourceUnder(Path.Combine(GetProductRoot(), "src", "ClearVision.Product.Infrastructure", "AI", "Agent")) +
                     ReadSourceUnder(Path.Combine(GetProductRoot(), "src", "ClearVision.Product.Infrastructure", "AI", "Tools")) +
                     ReadSourceUnder(Path.Combine(GetProductRoot(), "src", "ClearVision.Product.Core", "AI", "Tools"));
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
            "File.OpenRead",
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

    private static VisionAgentToolRegistry CreateRegistry()
    {
        var adapterRegistry = CreateAdapterRegistry();
        return new VisionAgentToolRegistry(
        [
            new FlowValidationTool(),
            new DryRunFlowTool(),
            new RuntimePreviewCaptureStubTool(adapterRegistry),
            new RuntimePreviewReplayStubTool(adapterRegistry)
        ]);
    }

    private static RuntimePreviewAdapterRegistry CreateAdapterRegistry()
    {
        return new RuntimePreviewAdapterRegistry(
        [
            new OfflineRuntimePreviewAdapter(new RuntimePreviewArtifactStore())
        ]);
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

    private static object ValidFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "<pending-camera-binding>" }),
                Operator("op_match", "TemplateMatching", new Dictionary<string, string> { ["TemplatePath"] = "<pending-template-path>" }),
                Operator("op_measure", "MeasureDistance"),
                Operator("op_out", "ResultOutput", new Dictionary<string, string> { ["Channel"] = "<pending-output-channel>" })
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_match", "Image"),
                Connection("op_match", "Pose", "op_measure", "PointA"),
                Connection("op_measure", "Distance", "op_out", "Input")
            }
        };
    }

    private static object BrokenFlow()
    {
        return new
        {
            operators = new object[]
            {
                Operator("op_cam", "ImageAcquisition", new Dictionary<string, string> { ["CameraBindingId"] = "mock-cam" })
            },
            connections = new object[]
            {
                Connection("op_cam", "Image", "op_missing", "Image")
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
